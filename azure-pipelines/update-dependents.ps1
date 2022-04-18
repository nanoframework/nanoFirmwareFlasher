"Updating dependency at nf-VS Code Extension" | Write-Host

# compute authorization header in format "AUTHORIZATION: basic 'encoded token'"
# 'encoded token' is the Base64 of the string "nfbot:personal-token"
$auth = "basic $([System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes("nfbot:$env:GH_TOKEN"))))"

# init/reset these
$commitMessage = ""
$prTitle = ""
$newBranchName = "develop-nfbot/update-dependencies/" + [guid]::NewGuid().ToString()
$packageTargetVersion = gh release view --json tagName --jq .tagName

# working directory is agent temp directory
Write-Debug "Changing working directory to $env:Agent_TempDirectory"
Set-Location "$env:Agent_TempDirectory" | Out-Null

$repoName = 'nf-VSCodeExtension'

# clone repo and checkout main branch
Write-Debug "Init and featch $repoName repo"


git clone --recurse-submodules --depth 1 https://github.com/nanoframework/$repoName repo 
Set-Location repo | Out-Null
git config --global gc.auto 0
git config --global user.name nfbot
git config --global user.email nanoframework@outlook.com
git config --global core.autocrlf true

Write-Host "Checkout main branch..."
git checkout --quiet main | Out-Null

####################
# VS Code extension

Write-Host "Updating nanoFramework.Tools.FirmwareFlasher version in VS Code extension..."

Set-Location nanoFirmwareFlasher | Out-Null

git checkout --quiet tags/$packageTargetVersion

Set-Location .. | Out-Null

#####################

"Bumping nanoFramework.Tools.FirmwareFlasher to $packageTargetVersion." | Write-Host -ForegroundColor Cyan                

# build commit message
$commitMessage += "Bumps nanoFramework.Tools.FirmwareFlasher to $packageTargetVersion.`n"
# build PR title
$prTitle = "Bumps nanoFramework.Tools.FirmwareFlasher to $packageTargetVersion"

# need this line so nfbot flags the PR appropriately
$commitMessage += "`n[version update]`n`n"

# better add this warning line               
$commitMessage += "### :warning: This is an automated update. Merge only after all tests pass. :warning:`n"

Write-Debug "Git branch" 

# create branch to perform updates
git branch $newBranchName

Write-Debug "Checkout branch" 

# checkout branch
git checkout $newBranchName

# check if anything was changed
$repoStatus = "$(git status --short --porcelain)"

if ($repoStatus -ne "")
{
    Write-Debug "Add changes" 

    # commit changes
    git add -A > $null

    Write-Debug "Commit changed files"

    git commit -m "$prTitle ***NO_CI***" -m "$commitMessage" > $null

    Write-Debug "Push changes"

    git -c http.extraheader="AUTHORIZATION: $auth" push --set-upstream origin $newBranchName > $null

    # start PR
    # we are hardcoding to 'main' branch to have a fixed one
    # this is very important for tags (which don't have branch information)
    # considering that the base branch can be changed at the PR there is no big deal about this 
    $prRequestBody = @{title="$prTitle";body="$commitMessage";head="$newBranchName";base="main"} | ConvertTo-Json
    $githubApiEndpoint = "https://api.github.com/repos/nanoframework/$repoName/pulls"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

    $headers = @{}
    $headers.Add("Authorization","$auth")
    $headers.Add("Accept","application/vnd.github.symmetra-preview+json")

    try 
    {
        $result = Invoke-RestMethod -Method Post -UserAgent [Microsoft.PowerShell.Commands.PSUserAgent]::InternetExplorer -Uri  $githubApiEndpoint -Header $headers -ContentType "application/json" -Body $prRequestBody
        'Started PR with dependencies update...' | Write-Host -NoNewline
        'OK' | Write-Host -ForegroundColor Green
    }
    catch 
    {
        $result = $_.Exception.Response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($result)
        $reader.BaseStream.Position = 0
        $reader.DiscardBufferedData()
        $responseBody = $reader.ReadToEnd();

        throw "Error creating PR: $responseBody"
    }
}
else
{
    Write-Host "Nothing udpate at nanoFramework Deployer."
}
