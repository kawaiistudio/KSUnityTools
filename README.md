# 🎨 KS Unity Tools
### *Professional VRChat Avatar Optimization Suite*

<div align="center">

![Version](https://img.shields.io/badge/version-1.1-7c3aed?style=for-the-badge)
![Unity](https://img.shields.io/badge/Unity-2019.4+-ff4757?style=for-the-badge)
![VRChat](https://img.shields.io/badge/VRChat-SDK_3.0+-00ff41?style=for-the-badge)
![License](https://img.shields.io/badge/license-MIT-7c3aed?style=for-the-badge)

**Transform your VRChat avatars from bloated to blazing fast** ⚡

[Features](#-features) • [Installation](#-installation) • [Usage](#-usage) • [Screenshots](#-screenshots)

</div>

---

## ✨ Features

### 🎨 **Prefab Optimizer** - *The Performance Powerhouse*
Transform heavy avatars into optimized masterpieces with surgical precision:

- **🔍 Smart Texture Analysis** - Instantly scan and list every texture in your prefab with detailed memory metrics
- **📊 Real-Time Size Comparison** - See "Before" vs "After" optimization stats side-by-side in MB/GB
- **⚡ Batch Optimization** - Apply compression settings to multiple textures simultaneously
- **🎯 Granular Control** - Individual texture selection with resolution, compression format, and mipmap info
- **💾 Memory Tracking** - Track both file size (disk) and runtime memory usage
- **🔧 Mesh Compression** - FBX mesh optimization with vertex/triangle count display
- **📈 Smart Compression** - DXT1/BC1 Crunch compression with adjustable quality (0-100)
- **🎨 Custom Max Size** - Set texture limits: 128, 256, 512, 1024, 2048, 4096
- **📋 Detailed Logging** - Real-time console output showing every optimization step

**Perfect for:**
- Reducing avatar file sizes by 50-80%
- Meeting VRChat performance rank requirements (Good/Excellent)
- Lowering VRAM usage for better FPS in crowded worlds
- Quick iteration during avatar development

---

### 🔄 **GLB to FBX Converter** - *The Format Wizard*
Seamlessly convert GLB models to VRChat-ready FBX with automatic material setup:

- **🤖 Auto Blender Detection** - Scans Registry, PATH, Steam, and standard locations automatically
- **🎨 Smart Material Processing** - Automatically connects textures to Principled BSDF nodes
- **📁 Organized Output** - Creates structured folders: `[Avatar Name] Converted to FBX/Textures/`
- **🖼️ Texture Extraction** - Exports all embedded textures as individual PNG files
- **⚡ One-Click Workflow** - Browse → Convert → Done in seconds
- **🔌 Material Recognition** - Auto-detects BaseColor, Normal, Metallic, Roughness, Emissive maps
- **💾 Persistent Settings** - Remembers your Blender path between sessions
- **📊 Real-Time Console** - Live Blender output streaming for transparency

**Perfect for:**
- Converting Booth/Gumroad GLB models to Unity-compatible FBX
- Preparing VRM/GLB imports for VRChat SDK
- Batch processing multiple avatar purchases
- Preserving material setups during file format conversion

---

## 📥 Installation

### Method 1: Unity Package Manager (Recommended)
1. Open Unity Package Manager (`Window > Package Manager`)
2. Click `+` → `Add package from git URL`
3. Paste: `https://github.com/kawaiistudio/KSUnityTools.git`
4. Click `Add`

### Method 2: Manual Installation
1. Download the latest release from [Releases](https://github.com/kawaiistudio/KSUnityTools/releases)
2. Extract the ZIP file
3. Copy the `Kawaii Studio` folder to your Unity project's `Assets` folder
4. Final path should be: `Assets/Kawaii Studio/Editor/`

---

## 🎮 Usage

### 🎨 Prefab Optimizer Workflow

1. **Launch the Tool**
   - Navigate to `Kawaii Studio > Prefab Optimizer` in Unity's top menu bar
   
2. **Select Your Avatar**
   - Drag and drop your avatar prefab into the "🎭 PREFAB" input field
   - The tool accepts any GameObject with Renderer components
   
3. **Analyze Assets**
   - Click `🔍 SCAN PREFAB` to generate a complete asset inventory
   - Review texture resolutions, compression formats, and memory usage
   - Check mesh vertex/triangle counts and current compression settings
   
4. **Configure Optimization**
   - **Texture Settings:**
     - **Max Texture Size:** Choose target resolution (128px to 4096px)
     - **Compression:** Select quality level (Uncompressed/Compressed/High Quality)
     - **Crunch Compression:** Enable for 30-50% additional size reduction
     - **Crunch Quality:** Fine-tune compression (0-100, higher = better quality)
     - **Generate Mipmaps:** Toggle for distance-based LOD
   
   - **Mesh Settings:**
     - **Mesh Compression:** Off/Low/Medium/High
     - Automatically enables polygon and vertex optimization
   
5. **Select Assets**
   - Use checkboxes to select individual textures/meshes
   - Or click "Select All" / "Select None" for batch operations
   - Expand categories with ▶/▼ arrows to see full lists
   
6. **Optimize!**
   - Click `🚀 OPTIMIZE` to apply settings instantly
   - Watch the console log for real-time progress
   - See memory savings calculated automatically
   - Review "Size After Opt." column for per-texture results

7. **Review Results**
   - Compare "Original Size" vs "Size After Opt." columns
   - Check percentage saved indicators (e.g., "-67.3%")
   - Read the optimization log for detailed changes
   - Test avatar in VRChat to verify performance improvements

---

### 🔄 GLB to FBX Converter Workflow

1. **Launch the Tool**
   - Navigate to `Kawaii Studio > GLB to FBX Converter` in Unity's menu
   
2. **Configure Blender Path**
   - Click `Auto-Detect` to automatically find Blender installation
   - Or click `Browse` to manually select `blender.exe`
   - Path is saved automatically for future sessions
   
3. **Select Input File**
   - Click `Browse` next to "GLB File" field
   - Navigate to your `.glb` model file
   - File name is shown in the console log
   
4. **Choose Output Location**
   - Click `Browse` next to "Output Folder"
   - Select where to save the converted FBX and textures
   - Tool creates a subfolder: `[ModelName] Converted to FBX/`
   
5. **Convert!**
   - Click `🚀 CONVERT GLB TO FBX` button
   - Watch the console for Blender's live output
   - Conversion typically takes 10-30 seconds
   - Success dialog appears when complete
   
6. **Import to Unity**
   - Navigate to your output folder
   - Drag the `_converted_FBX.fbx` file into Unity
   - Textures are in the `Textures/` subfolder
   - Materials are automatically set up with Principled BSDF

---

## 🎨 Screenshots

### VRChat Prefab Optimizer Interface
![Prefab Optimizer Interface](https://github.com/kawaiistudio/KSUnityTools/blob/main/prefab-optimizer.png)

**What You're Looking At:**
- **Top Section:** Clean purple-themed interface with avatar selection
- **Texture Compression Settings:** Granular control over max size, compression type, crunch settings, and mipmaps
- **Texture List (Collapsible):** Every texture with resolution, original size, optimized size, and percentage saved
- **Mesh Compression Settings:** FBX mesh optimization with compression level selector
- **Mesh List (Collapsible):** All meshes with vertex/triangle counts and individual compression options
- **Optimize Button:** One-click batch processing with real-time counter
- **Console Log:** Live output showing each optimization step with color-coded status messages
- **Memory Tracking:** Real-time display of disk size (MB) and runtime VRAM usage

**Key Visual Elements:**
- 🟣 Purple accents for headers and branding
- 🔴 Red action buttons for primary operations
- 🟢 Green success indicators and console text
- ⚫ Black console background for readability
- Clean checkbox selection system for individual assets

---

### GLB to FBX Converter Interface
![GLB Converter Interface](https://github.com/kawaiistudio/KSUnityTools/blob/main/glb-converter.png)

**What You're Looking At:**
- **Blender Path Section:** Auto-detection button with manual browse fallback
- **GLB File Input:** Simple file picker for source model
- **Output Folder Selector:** Choose where to save converted files
- **Convert Button:** Large, prominent action button with emoji indicator
- **Status Indicator:** Real-time conversion status (READY/Converting)
- **Console Output:** Live streaming of Blender's conversion process
- **Clean UI:** Minimal distractions, maximum functionality

**Key Visual Elements:**
- 🟣 Purple headers for section organization
- 🔴 Red convert button for immediate action
- 🟢 Green console text showing live Blender output
- ⚫ Dark theme for reduced eye strain
- Persistent settings automatically saved between sessions

**Workflow Highlights:**
- One-time Blender setup with auto-detection
- Three clicks to convert: Browse GLB → Browse Output → Convert
- Real-time feedback during 10-30 second conversion
- Automatic material node setup (no manual Blender work needed)
- Organized output structure for easy Unity import

---

## 🛠️ System Requirements

| Component | Requirement |
|-----------|-------------|
| **Unity** | 2019.4 or newer |
| **VRChat SDK** | 3.0 or later (for Prefab Optimizer) |
| **Blender** | 2.8+ (for GLB Converter) |
| **OS** | Windows, macOS, or Linux |
| **.NET** | 4.x or later |

---

## 📝 Changelog

### v1.1 (Current Release)
- ✨ Added "After Optimization" estimated size display
- 🎨 Improved UI colors for better readability (purple/red/green theme)
- 🏷️ Added version label to header (`v1.1`)
- 📊 Enhanced memory tracking with runtime VRAM calculations
- 🐛 Fixed minor UI layout alignment issues
- 🔧 Improved mesh compression workflow

### v1.0 (Initial Release)
- 🚀 Base texture optimization system with batch processing
- 📊 Real-time memory size display (file + runtime)
- 🔄 GLB to FBX converter with Blender auto-detection
- 🎨 Material processing and texture extraction
- 📋 Detailed console logging for transparency
- 💾 Persistent settings storage

---

## 🤝 Contributing

Contributions are welcome! Feel free to:
- 🐛 Report bugs via [Issues](https://github.com/kawaiistudio/KSUnityTools/issues)
- 💡 Suggest features or improvements
- 🔧 Submit pull requests
- ⭐ Star the repo if you find it useful!

---

## 📄 License

This project is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for details.

**TL;DR:** Free to use, modify, and distribute. Attribution appreciated but not required.

---

## 🌟 Support

If this tool saved you hours of manual optimization:
- ⭐ Star the repository
- 🐦 Share with fellow VRChat creators
- 💬 Join the discussion in Issues/Discussions
- ☕ [Buy me a coffee](https://ko-fi.com/kawaiistudio) *(optional)*

---

<div align="center">

### ⚡ Made with 💜 by Kawaii Studio

*Empowering VRChat creators with professional-grade tools*

[GitHub](https://github.com/kawaiistudio) • [VRChat Group](https://vrchat.com/home/group/grp_7bf987ee-2f4a-4eae-b9b5-c060b97250ab) • [Discord](https://discord.gg/xAeJrSAgqG) • [Telegram](https://t.me/kawaiistudio)

</div>
