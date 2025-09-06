# GSLM
Game Store Library Manager - plugins for RetroBat 


# 🎮 RetroBat Plugins – Scraper for Installed and Uninstalled Games  
*(Steam, Epic, Amazon, GOG)*

[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-blue?logo=dotnet)](https://dotnet.microsoft.com/en-us/download/dotnet/8.0/runtime)
[![WebView2](https://img.shields.io/badge/WebView2-required-success?logo=microsoftedge)](https://developer.microsoft.com/en-us/microsoft-edge/webview2/#download-section)
[![RetroBat](https://img.shields.io/badge/Compatible%20with-RetroBat-orange)](https://www.retrobat.org/)

---

## 📦 Prerequisites

Before using **Game Store Library Manager**, install:

- [Runtime .NET 8.0](https://dotnet.microsoft.com/en-us/download/dotnet/8.0/runtime)  
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/#download-section)

💡 **Tip**: create a `prereq.bat` file to automatically open the download pages:

---

## ⚙️ Main Features

- **📂 Loading Installation Files**  
  Adds the installation launchers to RetroBat by store folders.  
  ⚠️ For **Steam**, disable RetroBat’s internal option to avoid duplicates.

- **🔑 Authentication via Integrated WebView2**  
  Generates tokens after logging into the stores.  
  - For **Steam**, it also copies the API key.  
  - Activation possible:
    - via the **GSLM** menu at first launch  
    - via the shortcut in the store folder  
    - or with:
      ```bash
      GameStoreLibraryManager.exe -menu
      ```

- **🔒 Security (recommended)**  
  **DPAPI** option → encrypts tokens and the Steam API key, usable only on the current Windows session.  
  ℹ️ Automatic token renewal duration: **unknown**.

- **🎮 Loading Executables of Installed Games**  
  Already handled by RetroBat (**Steam, GOG, Epic, Amazon, EA Games**).  
  ⚠️ Risk of **conflict** or **different names** if used in parallel.

- **🖼️ Media and Metadata Scraping**  
  - Sources: **HFSPlay** and **Steam** (Steam preferred by default)  
  - The **first scrape** may take a long time depending on the library size  
  - **Gamelists** are automatically populated  

---

## 🔧 Additional Options

- **🤖 Auto-install / Semi-auto**  
  - Based on **UIA Automation** or **OCR + simulated clicks**  
  - Specific cases:
    - **GOG** → admin mode installation required  
    - Some games install dependencies at first launch  
  - **Alternative considered** (tested but not implemented) :
    - **GOGDL**-style method  
    - Extraction with **InnoExtract**  
    - Adding keys in **HKCU** (instead of HKLM) to generate `.lnk` files

- **🚀 Auto-boot After Installation**  
  - Automatically starts the game after installation  
  - Already handled by RetroBat for **Steam**  
  - The plugin manages the **other stores**  
  - **Prerequisite**: enable `localhost:1234` in RetroBat’s developer menu  

- **☁️ Amazon Luna**  
  - Integrated support via **WebView2** in a GSLM module  

---

## 📖 Notes

- The first launch may take several minutes (scraping + token generation).  
- Tokens are automatically renewed, but the exact duration is not confirmed.  
- For maximum security, enable **DPAPI** at first launch.  

---

