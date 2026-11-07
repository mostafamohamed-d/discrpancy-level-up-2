<#
.SYNOPSIS
  Checks that the add-in's one API assembly can attach to a running ETABS and to a running SAFE.

.DESCRIPTION
  The add-in drives both programs through a single assembly, CSiAPIv1.dll, because the copy
  ETABS ships and the copy SAFE ships are the same interfaces - same COM interface GUIDs, same
  method signatures, same enum values. That much can be checked without either program running,
  and it is checked in the build.

  What cannot be checked that way is whether a running program answers to those interfaces
  through the COM registry. That needs the program open, which is what this is for.

  Open ETABS with any model, or SAFE with any model, or both, and run this. It reads the model
  name and the present units and prints them. Nothing is written, and nothing is changed.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\Check-CsiAttach.ps1
#>

$ErrorActionPreference = 'Stop'

function Find-CsiApi {
    $roots = @($env:ProgramW6432, ${env:ProgramFiles}) | Where-Object { $_ } | Select-Object -Unique
    foreach ($root in $roots) {
        $dir = Join-Path $root 'Computers and Structures'
        if (-not (Test-Path $dir)) { continue }
        $found = Get-ChildItem $dir -Filter 'CSiAPIv1.dll' -Recurse -Depth 1 -ErrorAction SilentlyContinue |
                 Select-Object -First 1
        if ($found) { return $found.FullName }
    }
    return $null
}

$dll = Find-CsiApi
if (-not $dll) {
    Write-Host "CSiAPIv1.dll was not found. Install ETABS or SAFE, or edit this script." -ForegroundColor Red
    exit 1
}

Write-Host "Using $dll"
[void][System.Reflection.Assembly]::LoadFrom($dll)

$helper = New-Object CSiAPIv1.Helper

# SAFE really does register its API object as ETABSObject - a leftover from the API the two
# programs once shared. Both names are tried for each product, oldest install first.
$targets = [ordered]@{
    'ETABS' = @('CSI.ETABS.API.ETABSObject', 'CSI.ETABS.API.SapObject')
    'SAFE'  = @('CSI.SAFE.API.ETABSObject',  'CSI.SAFE.API.SapObject')
}

$anyFound = $false

foreach ($product in $targets.Keys) {
    $attached = $false

    foreach ($typeName in $targets[$product]) {
        try {
            $app = $helper.GetObject($typeName)
            if (-not $app) { continue }

            $model = $app.SapModel
            if (-not $model) { continue }

            $file = ''
            try { $file = $model.GetModelFilename($true) } catch { $file = '(unsaved)' }
            if (-not $file) { $file = '(unsaved)' }

            $units = $model.GetPresentUnits()

            Write-Host ("  {0,-6} ATTACHED via {1}" -f $product, $typeName) -ForegroundColor Green
            Write-Host ("         model : {0}" -f $file)
            Write-Host ("         units : {0}" -f $units)
            $attached = $true
            $anyFound = $true
            break
        }
        catch {
            # Not running, or not answering to this name. Try the next.
        }
    }

    if (-not $attached) {
        Write-Host ("  {0,-6} not running (or not answering the API)" -f $product) -ForegroundColor DarkYellow
    }
}

Write-Host ""
if ($anyFound) {
    Write-Host "Everything that attached above can be imported by the add-in." -ForegroundColor Green
} else {
    Write-Host "Nothing attached. Open a model in ETABS or SAFE and run this again." -ForegroundColor Yellow
}
