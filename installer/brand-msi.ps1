# brand-msi.ps1
# Finishes the MSI that "vpk pack" produced in .\Releases:
#   1. writes the branded dialog images (installer\banner.bmp, installer\logo.bmp)
#   2. fixes the desktop shortcut description, which vpk 1.2.158 leaves as a placeholder
# Works from any folder; the release workflow runs it too:  .\installer\brand-msi.ps1

$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$msi = Get-Item (Join-Path $root "Releases\*.msi") -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $msi) { throw "No .msi found in $root\Releases. Run vpk pack first." }
$msi = $msi.FullName

$banner      = (Resolve-Path (Join-Path $PSScriptRoot "banner.bmp")).Path
$logo        = (Resolve-Path (Join-Path $PSScriptRoot "logo.bmp")).Path
$description = "See where your disk space went"

$installer = New-Object -ComObject WindowsInstaller.Installer

# Windows Installer's COM object has no type info in PowerShell 7, so call it through reflection.
function Call($obj, $method, $params) {
    $obj.GetType().InvokeMember($method, [System.Reflection.BindingFlags]::InvokeMethod, $null, $obj, [object[]]$params)
}
function Prop($obj, $name, $params) {
    $obj.GetType().InvokeMember($name, [System.Reflection.BindingFlags]::GetProperty, $null, $obj, [object[]]$params)
}
function Run-Sql($db, $sql, $record = $null) {
    $view = Call $db "OpenView" @($sql)
    Call $view "Execute" @($record) | Out-Null
    Call $view "Close" @() | Out-Null
}
function Query-Column($db, $sql) {
    $view = Call $db "OpenView" @($sql)
    Call $view "Execute" @($null) | Out-Null
    $values = @()
    while (($rec = Call $view "Fetch" @()) -ne $null) { $values += (Prop $rec "StringData" @(1)) }
    Call $view "Close" @() | Out-Null
    return $values
}

$db = Call $installer "OpenDatabase" @($msi, 1)   # 1 = transact

# ---- 1. dialog images -------------------------------------------------------
$images = @{
    "WixUI_Bmp_Banner" = $banner; "WixUI_Bmp_Dialog" = $logo   # WiX 4/5 names
    "WixUIBannerBmp"   = $banner; "WixUIDialogBmp"   = $logo   # WiX 3 names
}
$binaries = Query-Column $db "SELECT Name FROM Binary"
$replaced = 0
foreach ($name in $binaries) {
    if (-not $images.ContainsKey($name)) { continue }
    $record = Call $installer "CreateRecord" @(1)
    Call $record "SetStream" @(1, $images[$name]) | Out-Null
    Run-Sql $db "UPDATE Binary SET Data = ? WHERE Name = '$name'" $record
    "Replaced image $name"
    $replaced++
}
if ($replaced -eq 0) {
    "No dialog images found. Binary table contains: $($binaries -join ', ')"
}

# ---- 2. desktop shortcut description -----------------------------------------
$hasProperty = (Query-Column $db "SELECT Property FROM Property WHERE Property = 'MsiDesktopShortcutDescription'").Count -gt 0
if ($hasProperty) {
    Run-Sql $db "UPDATE Property SET Value = '$description' WHERE Property = 'MsiDesktopShortcutDescription'"
} else {
    Run-Sql $db "INSERT INTO Property (Property, Value) VALUES ('MsiDesktopShortcutDescription', '$description')"
}
# In case the placeholder text is stored directly in the shortcut instead of through the property.
Run-Sql $db "UPDATE Shortcut SET Description = '$description' WHERE Description = '[MsiDesktopShortcutDescription]'"
"Set desktop shortcut description"

Call $db "Commit" @() | Out-Null

# Release the file so Windows Installer can open it afterwards.
$record = $null; $view = $null; $db = $null; $installer = $null
[GC]::Collect(); [GC]::WaitForPendingFinalizers()

"Done: $msi"
