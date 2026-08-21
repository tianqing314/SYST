$c = Get-Content 'E:\WPFCli\Output\SYST\src\04.TestSteps\SYST.TestSteps\ConST811A\ConST811A_LLP_Machine\ConST811A_LLP_Machine.cs'
foreach ($i in 1942..1977) {
    $line = $c[$i-1]
    Write-Host ("{0}: len={1} [{2}]" -f $i, $line.Length, $line)
}
