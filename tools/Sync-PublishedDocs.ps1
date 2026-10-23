#requires -Version 7.0
param([Parameter(Mandatory)][string]$OutputDirectory,[string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
function Get-SupportedPath([string]$Path){
    # This helper supports ordinary DOS paths, not namespace/device/UNC or short-name aliases.
    # GetFullPath alone does not canonicalize those aliases for ownership comparisons.
    if($Path -match '^[\\/]{2}' -or $Path -match '^[\\/]\?\??[\\/]' -or $Path.Contains('~') -or $Path -match '^[a-zA-Z]:(?![\\/])'){
        throw 'Unsupported publication path alias or namespace.'
    }
    $full=[IO.Path]::GetFullPath($Path).TrimEnd('\')
    if($full -notmatch '^[a-zA-Z]:\\' -or $full.Substring(3).Contains(':')){throw 'Only ordinary drive paths are supported.'}
    foreach($component in $full.Substring(3).Split('\')){
        if($component.EndsWith('.') -or $component -ne $component.TrimEnd()){throw 'Trailing-dot/space path aliases are unsupported.'}
    }
    return $full
}
$repository=Get-SupportedPath $RepositoryRoot
$output=Get-SupportedPath $OutputDirectory
$sourceDocs=Join-Path $repository 'docs'
$publishedDocs=[IO.Path]::GetFullPath((Join-Path $output 'docs'))
function Is-In([string]$Path,[string]$Root){$Path.Equals($Root,[StringComparison]::OrdinalIgnoreCase) -or $Path.StartsWith($Root+'\',[StringComparison]::OrdinalIgnoreCase)}
function Assert-NoReparse([string]$Path){
    $current=$Path
    while($current){
        if(Test-Path -LiteralPath $current){
            $item=Get-Item -LiteralPath $current -Force
            if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Refusing linked document path: $current"}
        }
        $parent=Split-Path $current -Parent
        if($parent -eq $current){break};$current=$parent
    }
}
if((Is-In $repository $output) -or (Is-In $output $sourceDocs) -or (Is-In $sourceDocs $publishedDocs)){
    throw 'Published docs must not overlap repository source docs.'
}
if(-not (Is-In $publishedDocs $output) -or $publishedDocs.Equals($output,[StringComparison]::OrdinalIgnoreCase)){
    throw 'Invalid resolved publication document directory.'
}
Assert-NoReparse $sourceDocs;Assert-NoReparse $output;Assert-NoReparse $publishedDocs
if(Test-Path -LiteralPath (Join-Path $output '.git')){throw 'A Git repository/worktree root cannot be a publication output.'}
if(-not(Test-Path -LiteralPath (Join-Path $output 'VirCre.Desktop.exe') -PathType Leaf)){
    throw 'Document synchronization requires an existing VirCre.Desktop.exe publish output.'
}
# docs/ is public; local material belongs under ignored .local/. Keep staged release
# pages eligible, exclude untracked drafts and legacy internal subdirectories/Markdown.
$tracked=@((& git -C $repository -c core.quotepath=false ls-files -z -- docs) -join "`n") -split "`0"
if($LASTEXITCODE -ne 0){throw 'Cannot enumerate version-controlled public documentation.'}
$pages=@()
foreach($relative in $tracked){
    if($relative -notmatch '^docs/[^/]+\.html$'){continue}
    $source=[IO.Path]::GetFullPath((Join-Path $repository $relative))
    if(-not(Test-Path -LiteralPath $source -PathType Leaf)){continue}
    if(-not(Is-In $source $sourceDocs)){throw "Invalid source document: $relative"}
    Assert-NoReparse $source
    $pages+=Get-Item -LiteralPath $source
}
if($pages.Count -eq 0){throw 'No tracked public HTML documents found; existing output was not changed.'}
if(Test-Path -LiteralPath $publishedDocs){
    if(-not(Test-Path -LiteralPath $publishedDocs -PathType Container)){throw 'Published docs path is not a directory.'}
    # Validate the absolute removal target and every child before recursive deletion.
    foreach($item in @(Get-ChildItem -LiteralPath $publishedDocs -Recurse -Force)){
        if(-not(Is-In $item.FullName $publishedDocs) -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)){
            throw "Unsafe entry in published docs: $($item.FullName)"
        }
    }
    Remove-Item -LiteralPath $publishedDocs -Recurse -Force
}
New-Item -ItemType Directory -Path $publishedDocs -Force | Out-Null
foreach($page in $pages){Copy-Item -LiteralPath $page.FullName -Destination (Join-Path $publishedDocs $page.Name) -Force}
Write-Output "Published public docs synchronized: $($pages.Count) HTML files."
