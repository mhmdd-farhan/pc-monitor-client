# Installing the Release Build (Windows 10/11)

This guide is for installing the **built release** on a Windows 10/11 PC using **PCMonitorClientSetup.msi**.

## Files you should have

You only need this file:
- `PCMonitorClientSetup.msi`

![Screenshot: Release folder showing PCMonitorClientSetup.msi](<PLACEHOLDER_IMAGE_RELEASE_FOLDER>)

## Step 0: Before you install (recommended checks)

1. **Log in to Windows with an account that can install apps**
   - If your PC is managed by an organization, you might need IT/admin approval.

2. **Close the app if it is already running**
   - If “Nadi Monitor” is open, close it from the system tray (if present) or end it in Task Manager.

![Screenshot: Task Manager showing Nadi Monitor / PCMonitorClient.exe](<PLACEHOLDER_IMAGE_TASK_MANAGER>)

3. **Move the installer to a local folder**
   - Example: `Downloads\\NadiMonitor\\`
   - Avoid installing from a network drive if possible.

![Screenshot: Installer folder inside Downloads](<PLACEHOLDER_IMAGE_INSTALLER_IN_DOWNLOADS>)

4. **(Optional) Unblock the MSI if it was downloaded**
   - Right-click `PCMonitorClientSetup.msi` → **Properties**
   - If you see an **Unblock** checkbox near the bottom, enable it → **Apply** → **OK**

![Screenshot: File Properties window with Unblock checkbox](<PLACEHOLDER_IMAGE_UNBLOCK>)

## Install using the MSI (Windows 10/11)

1. **Start the installer wizard**
   - Double-click `PCMonitorClientSetup.msi`
   - If Windows asks for permission (UAC), click **Yes**

![Screenshot: Windows UAC prompt](<PLACEHOLDER_IMAGE_UAC>)

2. **If you see a security warning (“Open File - Security Warning”)**
   - Confirm the publisher details if shown
   - Click **Run** to continue

![Screenshot: Open File - Security Warning dialog](<PLACEHOLDER_IMAGE_SECURITY_WARNING>)

3. **Welcome screen**
   - Click **Next**

![Screenshot: MSI Welcome screen](<PLACEHOLDER_IMAGE_MSI_WELCOME>)

4. **Installation folder**
   - Keep the default folder unless you have a specific standard path
   - Default is typically similar to: `C:\\MyApps\\Nadi Monitor\\`
   - If you get a “permission denied” / “access is denied” error later:
     - Re-run the installer as admin (see Step 7), or
     - Choose a folder you own, such as `C:\\Users\\<YOUR_USER>\\AppData\\Local\\MyApps\\Nadi Monitor\\`
   - Click **Next**

![Screenshot: MSI folder selection screen](<PLACEHOLDER_IMAGE_MSI_INSTALL_FOLDER>)

5. **Ready to install**
   - Click **Install**

![Screenshot: MSI Ready to Install screen](<PLACEHOLDER_IMAGE_MSI_READY>)

6. **Wait for installation to finish**
   - Keep the installer window open until it completes
   - This may take a minute depending on the PC

![Screenshot: MSI progress screen](<PLACEHOLDER_IMAGE_MSI_PROGRESS>)

7. **If installation fails due to permissions**
   - Right-click `PCMonitorClientSetup.msi` → **Run as administrator**
   - Complete the wizard again

![Screenshot: Context menu showing Run as administrator](<PLACEHOLDER_IMAGE_RUN_AS_ADMIN>)

8. **Finish**
   - Click **Finish**

![Screenshot: MSI Finish screen](<PLACEHOLDER_IMAGE_MSI_FINISH>)

## After install: Confirm it worked

1. **Check Start Menu**
   - Press the **Windows key**
   - Type `Nadi Monitor`
   - Click the app to open it

![Screenshot: Start menu search result for Nadi Monitor](<PLACEHOLDER_IMAGE_START_MENU_SEARCH>)

2. **Check Desktop shortcut (if created)**
   - Look for a shortcut named **Nadi Monitor** on the Desktop.

![Screenshot: Desktop shortcut for Nadi Monitor](<PLACEHOLDER_IMAGE_DESKTOP_SHORTCUT>)

3. **Check Installed Apps list**
   - Windows 11: **Settings → Apps → Installed apps**
   - Windows 10: **Settings → Apps → Apps & features**
   - Find **Nadi Monitor** (Publisher: Net Geometry)

![Screenshot: Installed apps list showing Nadi Monitor](<PLACEHOLDER_IMAGE_INSTALLED_APPS>)

## First launch: Required configuration (Supabase URL)

On startup, the app loads `SUPABASE_URL` from a `.env` file.

1. Launch **Nadi Monitor**
2. If you see an error like “SUPABASE_URL not found in .env file”, create the `.env` file next to the installed EXE.
3. Open the installation folder (examples):
   - `C:\\MyApps\\Nadi Monitor\\` (default)
   - Or the folder you chose during installation
4. Create a file named `.env`
5. Add the line:
   ```env
   SUPABASE_URL=your_supabase_project_url
   ```
6. Save the file, then close and reopen **Nadi Monitor**.

![Screenshot: .env file in the install directory](<PLACEHOLDER_IMAGE_ENV_FILE>)

## Uninstall (Windows 10/11)

1. Open **Settings**
2. Go to:
   - Windows 11: **Apps → Installed apps**
   - Windows 10: **Apps → Apps & features**
3. Search for **Nadi Monitor**
4. Click **Uninstall**
5. Confirm the uninstall prompts

![Screenshot: Uninstall flow in Settings](<PLACEHOLDER_IMAGE_UNINSTALL>)

## Troubleshooting

- **Windows blocks the MSI (“publisher could not be verified”)**
  - Verify where you got the installer from, then proceed via the security warning dialog.
- **Installation fails due to permissions**
  - Choose an install folder inside your user profile, or run the installer as an admin.
- **Windows says it needs a .NET runtime**
  - Install the required .NET runtime, then run the MSI again.
- **App says `SUPABASE_URL` is missing**
  - Ensure `.env` exists in the installed app folder and contains `SUPABASE_URL=...`.
