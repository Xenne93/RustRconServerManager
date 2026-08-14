==================================================
RconBackgroundService

This service runs in the background and handles the connection between the server app and the gameserver via the Xenne.Rcon library.
The service does the following things with the connections:

- Add
- Delete
- Edit
- Reconnect on disconnect

- Subscribes to events that are invoked by the Xenne.Rcon library.
- Processes received data that is received via Rcon and add's it to the database (on ServerId basis).

The RconBackgroundService is a service that is added in the DI via Program.cs
It's defined as a singleton.

services.AddSingleton<IRconBackgroundService, RconBackgroundService>();

===================================================