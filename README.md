# Cosmos DB NoSQL API - Inventory

## Introduction

This repository provides a code sample in .NET on how you might use a combination of Azure Functions and Cosmos DB to implement an inventory management process.

This demo contains 2 alternative ways to implement inventory management.

### Async Pattern Components:
- AsyncInventory Function (Http Trigger - Rest API)
- InventoryProcessor Function (CosmosDB Trigger)
- inventoryLedger container holds inventory events
- inventorySnapshot container holds inventory snapshots (current inventory position)
- leases container used by CosmosDB Trigger

### Sync Pattern:
- SyncInventory Function (Http Trigger - Rest API)
- syncInventory container holds inventory events and shapshots as separate documents

## Requirements to deploy
> Setup shell was tested on WSL2 (Ubuntu 22.04.2 LTS)

* <a href="https://learn.microsoft.com/en-us/cli/azure/install-azure-cli-linux?pivots=apt#option-1-install-with-one-command" target="_blank">Install Azure CLI</a>

* <a href="https://learn.microsoft.com/en-us/azure/azure-functions/functions-run-local?tabs=v4%2Clinux%2Ccsharp%2Cportal%2Cbash#install-the-azure-functions-core-tools" target="_blank">Install Azure Functions Core Tools</a>

* <a href="https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu#install-the-sdk" target="_blank">Install .NET SDK 8.0</a>

* <a href="https://git-scm.com/download/linux" target="_blank">Install Git</a>

* Install Zip: sudo apt install zip

## Setup environment

> The setup will provision and configure all the resources required.

* Sign in with Azure CLI

    ```bash
    az login
    ```

* Clone the repo
    ```bash
    git clone https://github.com/fonsecamar/cosmos-inventory-demo.git
    cd cosmos-inventory-demo/deploy/
    ```

* Run setup.sh with the appropriete parameters. Keep the API's URIs prompted when completed.
> Provide a non-existent resource group name. Setup will provision.

    ```bash
    #SAMPLE
    #./setup.sh 00000000-0000-0000-0000-000000000000 rg-my-demo SouthCentralUS

    ./setup.sh <subscription id> <resource group> <location>
    ```
> Setup has some pause stages. Hit enter to continue when prompted. 
> 
> It takes around 3min to provision and configure resoures.
>
> Resources created:
> - Resource group
> - Azure Blob Storage
> - Azure Cosmos DB account (1 database with 1000 RUs autoscale shared with 4 containers)
> - Azure Functions Basic Plan
> - Azure Log Analytics Workspace
> - Azure Application Insights

## Running the sample

You can call Function APIs from Azure Portal or your favorite tool.
> Async and Sync calls are similar. Just change the api path: /CreateAsyncInventoryEvent or /CreateSyncInventoryEvent, /GetAsyncSnapshot or /GetSyncSnapshot.

1. Creates initial inventory of a product
</br>
InventoryUpdated event will create or patch InventorySnapshot increasing onHand and availbleToSell quantities.

    ```bash
    curl --request POST "https://api-funcinv<suffix>.azurewebsites.net/api/CreateAsyncInventoryEvent" \
    --header 'Content-Type: application/json' \
    --data-raw '{
        "pk": "1-1000",
        "eventType": "InventoryUpdated",
        "eventDetails": {
            "productId": "1000",
            "nodeId": "1",
            "onHandQuantity": 1000
        }
    }'
    ```

1. Notifies items reserved
</br>
ItemReserved event will patch InventorySnapshot increasing activeCustomerReservations and decreasing availbleToSell quantities. Reservations can only occur if reservedQuantity < availableToSell.

    ```bash
    curl --request POST "https://api-funcinv<suffix>.azurewebsites.net/api/CreateAsyncInventoryEvent" \
    --header 'Content-Type: application/json' \
    --data-raw '{
        "pk": "1-1000",
        "eventType": "ItemReserved",
        "eventDetails": {
            "productId": "1000",
            "nodeId": "1",
            "reservedQuantity": 10
        }
    }'
    ```

1. Notifies order shipped
</br>
OrderShipped event will patch InventorySnapshot decreasing activeCustomerReservations and onHand quantities. Shippments can only occur if shippedQuantity <= activeCustomerReservations.

    ```bash
    curl --request POST "https://api-funcinv<suffix>.azurewebsites.net/api/CreateAsyncInventoryEvent" \
    --header 'Content-Type: application/json' \
    --data-raw '{
        "pk": "1-1000",
        "eventType": "OrderShipped",
        "eventDetails": {
            "productId": "1000",
            "nodeId": "1",
            "shippedQuantity": 10
        }
    }'
    ```
1. Notifies order cancelled
</br>
OrderCancelled event will patch InventorySnapshot decreasing activeCustomerReservations and increasing availableToSell quantities. Cancellations can only occur if cancelledQuantity <= activeCustomerReservations.

    ```bash
    curl --request POST "https://api-funcinv<suffix>.azurewebsites.net/api/CreateAsyncInventoryEvent" \
    --header 'Content-Type: application/json' \
    --data-raw '{
        "pk": "1-1000",
        "eventType": "OrderCancelled",
        "eventDetails": {
            "productId": "1000",
            "nodeId": "1",
            "cancelledQuantity": 10
        }
    }'
    ```

1. Notifies order returned
</br>
OrderReturned event will patch InventorySnapshot increasing returned and onHand quantities.

    ```bash
    curl --request POST "https://api-funcinv<suffix>.azurewebsites.net/api/CreateAsyncInventoryEvent" \
    --header 'Content-Type: application/json' \
    --data-raw '{
        "pk": "1-1000",
        "eventType": "OrderReturned",
        "eventDetails": {
            "productId": "1000",
            "nodeId": "1",
            "returnedQuantity": 10
        }
    }'
    ```

1. Get snapshot

    ```bash
    curl --request GET "https://api-funcinv<suffix>.azurewebsites.net/api/GetAsyncSnapshot/1-1000"
    ```

<br/>

# Clean Up

1. Delete the Resource Group to destroy all resources

<br/>

# How to Contribute

If you find any errors or have suggestions for changes, please be part of this project!

1. Create your branch: `git checkout -b my-new-feature`
2. Add your changes: `git add .`
3. Commit your changes: `git commit -m '<message>'`
4. Push your branch to Github: `git push origin my-new-feature`
5. Create a new Pull Request 😄