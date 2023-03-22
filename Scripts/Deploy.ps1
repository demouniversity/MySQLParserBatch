
$subscription = "<subscription name>"
#############################
#Either run this in AZ Cloud Shell or set up your local environment with AZ CLI

$rg = "<resource group>"
$region = "usgovvirginia"
$batchAccount = "batchparser" #Ensure this name matches the arm template deploy.json
$file = ".\mysqlparser.zip"
$appname = "mysqlparser" #Ensure this name matches the arm template deploy.json

#Login
az cloud set --name AzureUSGovernment
az login
az account set -s $subscription

#navigate to the repo folder
#cd ConvertMySqlFiles

#Create a resource group, if not already created
#az group create --name "$rg" --location "$region"


#deploy template (Create the function apps)
az deployment group create -g "$rg"  -f .\Templates\deploy.json

#PowerShell: User Assigned Managed Identity for Key Vault Access
$UAMSI= az deployment group show -g $rg -n deploy --query properties.outputs.managedIdentityAppName.value -o tsv
$FUNC_APP_NAME= az deployment group show -g $rg -n deploy --query properties.outputs.functionAppName.value -o tsv
#Install-Module Az.ManagedServiceIdentity
$userAssignedIdentityResourceId = Get-AzUserAssignedIdentity -ResourceGroupName $rg -Name $UAMSI | Select-Object -ExpandProperty Id
#Install-Module Az.Functions
$appResourceId = Get-AzFunctionApp -ResourceGroupName $rg -Name $FUNC_APP_NAME | Select-Object -ExpandProperty Id

$Path = "{0}?api-version=2022-03-01" -f $appResourceId
#### Apply User Assigned Managed Identity ####
Invoke-AzRestMethod -Method PATCH -Path $Path -Payload "{'properties':{'keyVaultReferenceIdentity':'$userAssignedIdentityResourceId'}}"


#Azure Batch is created in the deploy.json, next step is to the upload package
az batch application package create --application-name $appname `
                                    --name $batchAccount `
                                    --package-file $file `
                                    --resource-group $rg `
                                    --version-name "v1.0" 


      
Connect-AzAccount -Environment AzureUSGovernment
Set-AzContext -Subscription $subscription
# Ensure the package is set as the default, so it will be copied to nodes when the pool is created
## Got to Azure Batch -> Application -> click on application -> Settings -> Enter information
#Ensure the Azure Batch name matches the names in the deploy.json parameters
Set-AzBatchApplication -AccountName $batchAccount -ApplicationName $appname -DefaultVersion "v1.0" -ResourceGroupName $rg -DisplayName $appname


#Deploy the function app
cd src

func azure functionapp publish "$FUNC_APP_NAME" --dotnet


### Clean Up
#az group delete -n $rg -y

