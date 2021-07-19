# SplunkActivityMonitor

This application is capable of monitoring Foreground Window Changes and USB device insert/removal as well as device->USB file transfers. It will then report those events to Splunk (or any other web server) via the HTTP Event Collector.

The associated Splunk TA works in the following way:
* The application will start every 10 minutes via Task Scheduler for persistence. The application has logic that will kill it immediately if another instance is already running.
* The application is run as the "current user" (this grants access to the user's desktop context as well as any private/unshared disks they insert)
* The application is run with "high privileges" (this allows a hook into the Windows API)

## USB Monitoring
USB device insertion and removal is monitored via the __InstanceCreationEvent and __InstanceDeletionEvent objects within WQL, with watching implemented with a ManagementEventWatcher. Any time a USB is inserted or removed the application logs some metadata. An example of the output is:
     {
       action: Device Inserted
       caption: USB Mass Storage Device
       description: USB Mass Storage Device
       deviceid: USB\VID_0781&PID_5575\20043513030F55C2A8E1
       time: 2021-07-19 12:24:03.925
    } 

Separately, file modifications occurring on the USB are also logged. This application will only log filesystem changes that occur on the USB device to achieve data loss prevention goals. This is implemented with a FileSystemMonitor, which allows us to actively listen for new files, changed files, deleted files, and renamed files. An example of the output for a renamed file is:
    {
      action: Renamed
      fullpath: H:\myNewFile
      md5: D41D8CD98F00B204E9800998ECF8427E
      name: myNewFile
      oldpath: H:\myOldFile
      sha256: DDD4A2BA44C312AA4F2C7506A388CC2CA7F1CAEC60C3C6D80ED8A9F0B43D529C
      time: 2021-07-19 12:28:58.553
   } 

## Foreground Window Monitoring
Application utilises SetWinEventHook to write an event to Splunk on the EVENT_SYSTEM_FOREGROUND Windows event. 
An example of the output is:
     {
       directory: C:\Program Files\Mozilla Firefox\firefox.exe
       name: Firefox
       pid: 4480
       time: 2021-07-19 12:13:32.062
       title: Chess - Twitch — Mozilla Firefox
       user: DESKTOP-GPUBG06\andre
       version: 89.0.2
    } 

## Configuration
You can configure the tool using SplunkActivityMonitor.exe.config. This XML file contains all the configuration you need to perform the following tasks:
* Set the target web server 
* Set the HTTP Event Collector token
* Set the Splunk index and sourcetypes
* Enable or disable USB or Foreground Window monitoring components
* Allow bad SSL certs 
* Enable debug logging

## Contact
andrew@atunnecliffe.com
