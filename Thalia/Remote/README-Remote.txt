THALIA REMOTE
=============
Thalia on this laptop, using the MPAI as a Service of your desktop. The Apps run on the desktop;
the microphone, the camera and the screen are this laptop's.

1. Install Tailscale on this laptop (https://tailscale.com/download) and sign in with the SAME account as on the desktop.
   It works on any network, including a phone hotspot.
2. On the desktop, run D:\AI\Thalia\Remote\Thalia-Service.bat and leave its window open.
3. On this laptop, double-click Thalia-Remote.bat. Thalia fills the screen; Return to Windows brings the desktop back.

remote.json holds the address of your desktop and YOUR TOKEN: anybody with this folder can use your desktop's Service
(while it is connected to your Tailscale network). Keep this folder private; delete the zip after copying it.
