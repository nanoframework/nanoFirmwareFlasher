
# Common functions used by the other scripts

function Check-DirectoryExists {
    param (
        [string]$rootDirectory
    )

    return Test-Path -Path $rootDirectory -PathType Container
}

function Find-ReplaceInFiles {
    param (
        [string]$rootDirectory,
        [string]$sourceString,
        [string]$targetString
    )

    Get-ChildItem -Recurse -File $rootDirectory | ForEach-Object {
        $filePath = $_.FullName

        Find-ReplaceInFile -filePath $filePath -sourceString $sourceString -targetString $targetString
    }

    Write-Host "String replacement completed."
}

function Find-ReplaceInFile {
    param (
        [string]$filePath,
        [string]$sourceString,
        [string]$targetString
    )

    # Get the original encoding of the file
    $sr = New-Object System.IO.StreamReader($filePath, $true)
    [char[]] $buffer = new-object char[] 3
    $sr.Read($buffer, 0, 3)  
    $originalEncoding = $sr.CurrentEncoding
    $sr.Close()

    # Making it simple as we only want to preserve UTF8 encoding
    if ( $originalEncoding.BodyName -eq "utf-8" ) {
      $fileEncoding = "UTF8" 
    }
    else {
      $fileEncoding = "ASCII" 
    }
    
    # Read the content of the file using the original encoding
    $content = Get-Content -Path $filePath -Raw -Encoding $fileEncoding
    
    # Perform the replacement
    $newContent = $content -replace [regex]::Escape($sourceString), $targetString

    if ($content -ne $newContent) {
        Write-Host "Replacing in $filePath"
        
        # Save the modified content back to the file using the original encoding
        Set-Content -Path $filePath -Value $newContent -Encoding $fileEncoding
    }
}