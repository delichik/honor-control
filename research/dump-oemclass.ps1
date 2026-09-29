$ErrorActionPreference = 'SilentlyContinue'
foreach ($name in 'OemWMIMethod','OemWMIfun','OemWMIfunEx','OemWMIEvent') {
    Write-Host "########## $name ##########"
    $cls = Get-CimClass -Namespace root/WMI -ClassName $name
    if ($cls) {
        Write-Host "--- Properties ---"
        $cls.CimClassProperties | ForEach-Object {
            Write-Host ("  {0} : {1} {2}" -f $_.Name, $_.CimType, ($_.Qualifiers | ForEach-Object { $_.Name + '=' + $_.Value }) -join ' ')
        }
        Write-Host "--- Methods ---"
        $cls.CimClassMethods | ForEach-Object {
            Write-Host ("  METHOD: " + $_.Name)
            Write-Host "    IN:"
            $_.Parameters | Where-Object { $_.CimType -notmatch 'Out' -or $_.Qualifiers.Name -match 'ID|in' } | ForEach-Object {
                Write-Host ("      {0} : {1} [{2}]" -f $_.Name, $_.CimType, (($_.Qualifiers | ForEach-Object { $_.Name }) -join ','))
            }
        }
    } else {
        Write-Host "  (class not found)"
    }
}
