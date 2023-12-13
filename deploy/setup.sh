SUBSCRIPTION_ID=$1
RESOURCE_GROUP=$2
LOCATION=$3
SUFFIX=$4

echo 'Subscription Id     :' $SUBSCRIPTION_ID
echo 'Resource Group      :' $RESOURCE_GROUP
echo 'Location            :' $LOCATION
#echo 'Deploy Suffix       :' $SUFFIX

echo 'Validate variables above and press any key to continue setup...'
read -n 1

#Start infrastructure deployment
cd ../infrastructure
echo "Directory changed: '$(pwd)'"

az account set --subscription $SUBSCRIPTION_ID
az account show

echo 'Validate current subscription and press any key to continue setup...'
read -n 1

RGCREATED=$(az group create \
                --name $RESOURCE_GROUP \
                --location $LOCATION \
                --query "properties.provisioningState" \
                -o tsv)

if [ "$RGCREATED" != "Succeeded" ] 
then
    echo 'Resource group creation failed! Exiting...'
    exit
fi

INFRADEPLOYED=$(az deployment group create \
                    --name CosmosDemoDeployment \
                    --resource-group $RESOURCE_GROUP \
                    --template-file ./main.bicep \
                    --query "properties.provisioningState" \
                    -o tsv)
#                    --parameters suffix=$SUFFIX \

if [ "$INFRADEPLOYED" != "Succeeded" ] 
then
    echo 'Infrastructure deployment failed! Exiting...'
    exit
fi

echo 'Press any key to continue setup...'
read -n 1

cd ../src
echo "Directory changed: '$(pwd)'"

cd cosmos-inventory-api

FUNCAPINAME=$(az functionapp list --resource-group $RESOURCE_GROUP --query "[?starts_with(name, 'api-')].name" -o tsv)
func azure functionapp publish $FUNCAPINAME --dotnet-version '8.0'

cd ../cosmos-inventory-worker

FUNCWORKERNAME=$(az functionapp list --resource-group $RESOURCE_GROUP --query "[?starts_with(name, 'worker-')].name" -o tsv)
func azure functionapp publish $FUNCWORKERNAME --dotnet-version '8.0'

cd ../../deploy

echo ""
echo "***************************************************"
echo "*************  Deploy completed!  *****************"
echo "Next steps:"
echo "1. Call APIs"
echo "***************************************************"