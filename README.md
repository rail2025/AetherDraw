# AetherDraw
AetherDraw is a XivLauncher/Dalamud plugin.

AetherDraw allows you to create and share multi-page strategy diagrams for raid and trial encounters, either by saving plan files or collaborating with other users in a real-time live session. AetherDraw is designed for raid leaders and players who need a clear, visual way to explain and understand encounter mechanics.

The goal of this plugin is to make complex strategies easier to communicate by providing a shared visual whiteboard.

AetherDraw is for creating and collaborating on plans. For displaying the saved images as an in-game reference during a fight, please use the companion plugin, [WDIGViewer](https://github.com/rail2025/WDIGViewer).
<br>
<br>

![GitHub Downloads (all assets, all releases)](https://img.shields.io/github/downloads/rail2025/AetherDraw/total?logoColor=brightgreen)
<br>
![GitHub Downloads (all assets, latest release)](https://img.shields.io/github/downloads/rail2025/AetherDraw/latest/total?labelColor=blue)
<br>
![aetherdraw - v1 3 0 0](https://github.com/user-attachments/assets/5d8e35bf-d9f0-435f-b9e2-a3cadd5619c9)
 <br>
<br>
![draw area example](https://github.com/user-attachments/assets/85fa34e4-aa57-494c-8f58-34ffd09c4101)
<br>
![export example](https://github.com/user-attachments/assets/93cbb47b-b749-4409-a878-2e76c73756a4)

<br>
<img width="1019" height="804" alt="raidplan urls" src="https://github.com/user-attachments/assets/920bdb22-93a2-4c73-9997-effa1533bab7" />

<br>
<img width="911" height="765" alt="emoji" src="https://github.com/user-attachments/assets/eae44084-77ec-47ad-8d4f-aa801e2e6918" />



<br>![save plan example](https://github.com/user-attachments/assets/4033928c-234c-4e07-a56e-db7506633b6b)

<br>


open with /aetherdraw  <br>
# Installation
Enter /xlsettings in the chat window and go to the Experimental tab in the opening window.
click the get testing builds:
![testing builds](https://github.com/user-attachments/assets/5fc52921-06d0-4545-9038-86f0f93f8a98)
Click on the Save-icon in the bottom right.
Enter /xlplugins in the chat window and look for AetherDraw under the Testing Available and install. 
<br>
## Bottom bar buttons:
<br>
- You can save the plans as editable plans using the save/load plan buttons, and send those files to other users so they can edit. <br>
<br>
- You can save the plans as flat images(no editing). If you have 4 pages saved as "boss phase 1", it will get saved as 1-boss phase 1.png, 2-boss phase 1.png, 3-boss phase 1.png, etc. <br>
<br>
- You can create/join a live session and collaborate with other AetherDraw users in real time (images update after mouse release) <br>

## >>>>for party quick sync, you must be in a party, and in a duty. For passphrase, there is no such restriction.
                                                                                                                  
<br>

<br> The passphrase is a random FFXIV themed sentence that should be safe to paste in chat with plausible deniability. You may use your own passphrase, 123abc123, hotdogsaresandwiches, Lalafels are potatoes in disguise, etc. The plugin just makes it more convenient to share in game. 
<br>
#### Be aware there are NO user lists, or any way to identify who drew anything in the plugin. This is by design to protect your anonymity. This comes with some tradeoffs, but such is life.

<br>
<br>
If you save as image file into the PluginImages folder for WDIGViewer, when you use the open WDIG button and rescan/reload all images, the raid plan images will appear there so you can use it right away!
<br>
WDIGViewer is here:<br>
https://github.com/rail2025/WDIGViewer
<br>
<br>

# 🔐 Privacy & Security

AetherDraw is designed around user privacy, anonymity, and plausible deniability. It does not collect or expose any identifying information about the user, their character, or their connection.
<br>

🚫 What AetherDraw Does Not Do

    ❌ Does not transmit your character name, world, content ID, or any in-game identifier.

    ❌ Does not embed metadata or source identifiers in drawing data.

    ❌ Does not store any data persistently (no disk writes, no database).

    ❌ Does not provide a list of connected users to others in the room.

    ❌ Does not log or inspect IP addresses or client origins.

    ❌ Does not send or receive any communication outside the room scope.

✅ What AetherDraw Does

    ✅ Sends drawing strokes as binary messages to a relay-only WebSocket server.

    ✅ Relays messages only to other clients in the same room (by hash or passphrase).

    ✅ Provides two modes of connection:

        Party Sync: Uses a SHA-256 hash of sorted Party member IDs (for in-party sessions).

        Passphrase Mode: Uses a readable sentence as a room key (for cross-world or anonymous sessions). Or make your own generic phrase "123abc123", "TacoTuesday", "Tomestone cap is too low", etc.

    ✅ Uses WebSocket over TLS (WSS) for encrypted communication.

    ✅ Implements strict room expiry and automatic cleanup:

        ⏱️ Rooms expire after 2 hours, or after 3 minutes alone.

        🧹 Room data is never stored beyond process memory.

🌐 Relay Server Design

The official server (wss://aetherdraw-server.onrender.com/ws) is open source:

    🧠 Stateless by design — acts as a dumb relay.

    🗑️ Deletes rooms on timeout or when empty.

    ❌ No personal information is ever logged or stored.

    📜 No session tokens, cookies, or identifiers.

    🛑 No awareness of user identities or origins.

    ✅ You can self-host the server if desired.

#### 🧅 Threat Model

|Scenario |	What Happens?	| Notes|
|---|---|---|
|Passphrase guessed |	Attacker joins your room	|Use unique/generated phrases for private syncs.
|Packet sniffing	| Can see binary draw data	|No user-identifying metadata present.
| Client impersonation |	Can send drawing data	|All clients are anonymous by design — spoofing ≈ regular use.
| Room abuse (e.g. griefing in public)	| No protection|	No moderation due to intentional lack of user identifiers.

#### ⚙️ Configuration & Behavior
|Feature |	Behavior |
|---|---|
Room caps | 8 users for Party Sync, 48 for passphrase-based rooms
Client list	 |  Not exposed or tracked
Drawing data	| Binary-only, no tags, no user linkage
Moderation |	Not implemented — would compromise anonymity
Room control |	None — anyone with the phrase or hash can draw

#### ❓ Why No Moderation or Visibility?

AetherDraw is designed for:

    Anonymity: No one can tell who is using the plugin.

    Deniability: Passphrases read like in-character phrases.

    Utility: Enables real-time collaborative drawing across any world or data center.

This means:

    No usernames or "ownership" of rooms.

    No in-room kicking or reporting features.

    No global directories or room listings.

If your use case requires tracking, user control, or session auditing, this plugin is intentionally not built for that.
