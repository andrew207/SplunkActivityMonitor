# SplunkActivityMonitor

Application utilises SetWinEventHook to write an event to Splunk on the EVENT_SYSTEM_FOREGROUND Windows event. 
An example of the output is:

    {
    	"time": "2018-04-23 18:38:47.576",
    	"user": "DESKTOP-LPASN32\\andre",
    	"title": "SplunkActivityMonitor - Microsoft Visual Studio ",
    	"filename": "C:\\Program Files (x86)\\Microsoft Visual Studio\\2017\\Enterprise\\Common7\\IDE\\devenv.exe",
    	"pid": "3132"
    }

* The application is run as the current user (this grants access to the user's desktop context)
* The application is run with "high privileges" (this allows a hook into the Windows API)
* The application will start every 10 minutes for persistence. The application has logic that will kill it immediately if another instance is already running.