schtasks /Create /rl HIGHEST /it /tn "SplunkActivityMonitorStarter" /sc MINUTE /mo 10 /ru "builtin\users" /tr "'%PROGRAMFILES%\Splunk\etc\apps\TA-WindowsActivityMonitor\bin\SplunkActivityMonitor.exe'" /f