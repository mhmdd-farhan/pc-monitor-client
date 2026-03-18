# PC Monitoring System – Client App (NADI)

A lightweight client application for monitoring PC performance and status as part of the **NADI Monitoring System**.  
This client app communicates with the NADI server to send real-time PC metrics (CPU, memory, storage, network) and receive remote commands (shutdown, restart, lock).

## 📋 Table of Contents
- [PC Monitoring System – Client App (NADI)](#pc-monitoring-system--client-app-nadi)
  - [📋 Table of Contents](#-table-of-contents)
  - [Prerequisites](#prerequisites)
  - [Architecture](#architecture)
    - [Key Technologies](#key-technologies)
    - [Key Dependencies](#key-dependencies)
    - [Core Components](#core-components)
  - [Project Structure](#project-structure)
  - [Development Setup](#development-setup)
  - [Building for Release](#building-for-release)
  - [Installing the Release Build (Windows 10/11)](#installing-the-release-build-windows-1011)
  - [Mac User Note](#mac-user-note)
  - [License](#license)

## Prerequisites

To develop and run this application, you need the following environment:

- **Operating System**: Windows 10 or Windows 11 (Required for WPF).
- **IDE**: [Visual Studio 2022](https://visualstudio.microsoft.com/vs/) (Recommended).
  - Workload: **.NET Desktop Development**.
  - Extension: **Microsoft Visual Studio Installer Projects 2022** (for building the installer).
- **SDK**: [.NET 8.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).

## Architecture

This project is a **Windows Presentation Foundation (WPF)** application built with **.NET 8**.

### Key Technologies
- **Frontend**: WPF (XAML for UI, C# for logic).
- **Backend Communication**: [Supabase](https://supabase.com/) for database and real-time commands.
- **Real-time**: Uses Supabase Realtime channels to listen for remote commands (`shutdown`, `restart`, `lock`, `unlock`).

### Key Dependencies
- **Supabase**: Handles authentication, database, and real-time subscriptions.
- **NAudio & SIPSorcery**: Used for audio handling and potential VoIP/WebRTC features.
- **SharpDX**: Provides access to DirectX APIs for graphics/performance monitoring.
- **InputSimulator**: Allows simulating keyboard/mouse input programmatically.
- **dotenv.net**: Loads environment variables from a `.env` file.

### Core Components
- **App.xaml.cs**: Application entry point. Handles single-instance logic (Mutex) and auto-updates via GitHub Releases.
- **MainWindow.xaml.cs**: Main UI logic. Initializes monitoring, handles resource cleanup.
- **RealtimeMessenger.cs**: Manages the connection to Supabase Realtime to listen for server commands.
- **KeyboardHook.cs**: Implements low-level keyboard hooks (likely for monitoring user activity or locking mechanisms).

## Project Structure

- **`PCMonitorClient.sln`**: The Visual Studio solution file containing all projects.
- **`PCMonitorClient/`**: The main WPF application source code.
    - `Assets/`: Images and static resources.
    - `Properties/`: Assembly info and resources.
    - `*.xaml`: UI definitions.
    - `*.cs`: Application logic (Code-behind and helpers).
- **`PCMonitorClientSetup/`**: A Visual Studio Setup Project (`.vdproj`) for creating the `.msi` installer.

## Development Setup

1.  **Clone the Repository**
    ```bash
    git clone https://github.com/mhmdd-farhan/pc-monitor-client.git
    cd pc-monitor-client
    ```

2.  **Open in Visual Studio**
    - Double-click `PCMonitorClient.sln` to open the solution.

3.  **Configure Environment Variables**
    - Create a new file named `.env` in the root of the `PCMonitorClient` project folder (where `PCMonitorClient.csproj` is located).
    - Add your Supabase URL:
      ```env
      SUPABASE_URL=your_supabase_project_url
      ```
    - *Note: Ensure the file properties in VS are set to "Copy to Output Directory: Copy always" or similar if not automatically handled.*

4.  **Restore Packages**
    - Visual Studio should automatically restore NuGet packages. If not, right-click the solution in Solution Explorer and select **Restore NuGet Packages**.

5.  **Run the Application**
    - Set `PCMonitorClient` as the StartUp Project.
    - Press **F5** or click the **Start** button to run in Debug mode.

## Building for Release

To create a deployable installer (`.msi` or `.exe`):

1.  **Switch Configuration**
    - Change the build configuration from `Debug` to `Release` in the Visual Studio toolbar.

2.  **Build Main Project**
    - Right-click `PCMonitorClient` project -> **Build**.

3.  **Build Installer**
    - Right-click `PCMonitorClientSetup` project -> **Build**.
    - *Note: You must have the "Microsoft Visual Studio Installer Projects" extension installed.*

4.  **Locate Installer**
    - The output will be in `PCMonitorClientSetup/Release/`.
    - You will find `PCMonitorClientSetup.msi` and `setup.exe`.

## Installing the Release Build (Windows 10/11)

See [INSTALL_WINDOWS.md](INSTALL_WINDOWS.md) for the full step-by-step guide (with screenshot placeholders).

## Mac User Note

⚠️ **This application is Windows-only.**

It uses **WPF (Windows Presentation Foundation)** and native Windows APIs (e.g., `user32.dll`, `kernel32.dll` via P/Invoke) which are **not compatible with macOS**.

To develop or run this project on a Mac, you must use a virtualization solution:
- **Parallels Desktop** (Recommended for M1/M2/M3 Macs).
- **VMware Fusion**.
- **Boot Camp** (Intel Macs only).

You cannot run this project natively using `dotnet run` on macOS.

## License

This project is licensed under the MIT License.
