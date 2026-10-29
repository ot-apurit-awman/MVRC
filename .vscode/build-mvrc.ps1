param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$Deploy
)

$ErrorActionPreference = 'Stop'
$workspacePath = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $workspacePath 'MyWinterCarVR.csproj'
$requiredReferences = @(
    'Assembly-CSharp.dll',
    'Assembly-CSharp-firstpass.dll',
    'UnityEngine.dll',
    'UnityEngine.UI.dll',
    'PlayMaker.dll',
    'MSCLoader.dll',
    'cInput.dll',
    '0Harmony.dll'
)

function Stop-Build([string]$Message) {
    throw $Message
}

try {
    $referenceValue = $env:MVRC_GAME_REFS
    if (-not $referenceValue) {
        $localPropsPath = Join-Path $workspacePath 'Local.props'
        if (Test-Path -LiteralPath $localPropsPath -PathType Leaf) {
            $referenceNode = Select-Xml -LiteralPath $localPropsPath -XPath '/*[local-name()="Project"]/*[local-name()="PropertyGroup"]/*[local-name()="MVRC_GAME_REFS"]' |
                Select-Object -First 1
            if ($referenceNode) {
                $referenceValue = $referenceNode.Node.InnerText
            }
        }
    }
    if (-not $referenceValue) {
        Stop-Build 'MVRC_GAME_REFS is not set. Set it to the folder containing the private game reference DLLs before building, or configure it in Local.props.'
    }

    $referencePath = [Environment]::ExpandEnvironmentVariables($referenceValue)
    if (-not (Test-Path -LiteralPath $referencePath -PathType Container)) {
        Stop-Build "MVRC_GAME_REFS does not point to an existing folder: $referencePath"
    }

    $missingReferences = @($requiredReferences | Where-Object {
        -not (Test-Path -LiteralPath (Join-Path $referencePath $_) -PathType Leaf)
    })
    if ($missingReferences.Count -gt 0) {
        $missingMessage = "The MVRC_GAME_REFS folder is missing these required reference DLLs:`n"
        $missingMessage += ($missingReferences | ForEach-Object { "  $_" }) -join "`n"
        Stop-Build $missingMessage
    }

    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        Stop-Build "Project file was not found: $projectPath"
    }

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnet) {
        $buildProgram = $dotnet.Source
        $buildArguments = @(
            'build', $projectPath,
            '--configuration', $Configuration,
            '--nologo',
            '/p:Platform=AnyCPU',
            "/p:MVRC_GAME_REFS=$referencePath",
            '/p:TargetFrameworkProfile=',
            '/p:PostBuildEvent='
        )
    }
    else {
        $vswhereCandidates = @(
            (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'),
            (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\Installer\vswhere.exe')
        )
        $vswherePath = $vswhereCandidates | Where-Object {
            $_ -and (Test-Path -LiteralPath $_ -PathType Leaf)
        } | Select-Object -First 1
        if (-not $vswherePath) {
            Stop-Build 'The dotnet CLI was not found, and Visual Studio MSBuild could not be located with vswhere. Install the .NET SDK or Visual Studio Build Tools.'
        }

        $msbuildPath = & $vswherePath -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
            Select-Object -First 1
        if (-not $msbuildPath) {
            Stop-Build 'Visual Studio was found, but its MSBuild component is not installed.'
        }

        $buildProgram = $msbuildPath
        $buildArguments = @(
            $projectPath,
            '/t:Build',
            '/nologo',
            "/p:Configuration=$Configuration",
            '/p:Platform=AnyCPU',
            "/p:MVRC_GAME_REFS=$referencePath",
            '/p:TargetFrameworkProfile=',
            '/p:PostBuildEvent='
        )
    }

    Write-Host "Building $projectPath ($Configuration)..."
    & $buildProgram @buildArguments
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    $outputPath = Join-Path $workspacePath "bin\$Configuration\MVRC.dll"
    if (-not (Test-Path -LiteralPath $outputPath -PathType Leaf)) {
        Stop-Build "Build completed without producing the expected output DLL: $outputPath"
    }
    Write-Host "Output DLL: $outputPath"

    if ($Deploy) {
        if (-not $env:MVRC_MODS_DIR) {
            Stop-Build '-Deploy requires MVRC_MODS_DIR to be set to the game Mods folder.'
        }
        $modsPath = [Environment]::ExpandEnvironmentVariables($env:MVRC_MODS_DIR)
        if (-not (Test-Path -LiteralPath $modsPath -PathType Container)) {
            Stop-Build "MVRC_MODS_DIR does not point to an existing folder: $modsPath"
        }
        $nativePath = Join-Path $workspacePath 'openvr_api.dll'
        if (-not (Test-Path -LiteralPath $nativePath -PathType Leaf)) {
            Stop-Build "The native runtime DLL was not found: $nativePath"
        }
        Copy-Item -LiteralPath $outputPath -Destination $modsPath -Force
        Copy-Item -LiteralPath $nativePath -Destination $modsPath -Force
        Write-Host "Deployed MVRC.dll and openvr_api.dll to $modsPath"
    }

    Write-Host 'Build OK'
    exit 0
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
