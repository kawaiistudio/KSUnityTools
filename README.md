<div align="center">

![Kawaii Studio Banner](https://github.com/kawaiistudio/KSUnityTools/blob/main/Documentation~/logo%20KS.png)

# 🎨 KS Unity Tools
### *Professional VRChat Avatar & Video Optimization Suite*

![Version](https://img.shields.io/badge/version-2.0-7c3aed?style=for-the-badge)
![Unity](https://img.shields.io/badge/Unity-2019.4+-ff4757?style=for-the-badge)
![VRChat](https://img.shields.io/badge/VRChat-SDK_3.0+-00ff41?style=for-the-badge)
![License](https://img.shields.io/badge/license-MIT-7c3aed?style=for-the-badge)

**Transform your VRChat avatars and videos from bloated to blazing fast** ⚡

[Features](#-features) • [Installation](#-installation) • [Usage](#-usage) • [Screenshots](#-screenshots)

</div>

---
## 🔒 Security

### VirusTotal Scan - ✅ Clean

The file was scanned by VirusTotal and is **100% safe**:

- **SHA-256:** `9188186f829ce5ac52f2c758eeb553a8dc39b627ba53ea165e536afa558ec73e`
- **Detections:** 0/73 antivirus engines
- **Status:** ✅ No threats detected

🔗 [**View the full VirusTotal report**](https://www.virustotal.com/gui/file/9188186f829ce5ac52f2c758eeb553a8dc39b627ba53ea165e536afa558ec73e?nocache=1)

<details>
<summary>📊 Scan details</summary>

| Scanner | Result |
|--------|--------|
| Kaspersky | ✅ Clean |
| McAfee | ✅ Clean |
| Avast | ✅ Clean |
| BitDefender | ✅ Clean |
| Norton | ✅ Clean |
| Windows Defender | ✅ Clean |
| **Total** | **0/73 detections** |

*Last checked: 2024*
</details>

## ✨ Features

### 🎨 **Prefab Optimizer** - *The Performance Powerhouse*
Transform heavy avatars into optimized masterpieces with surgical precision:

- **🔍 Smart Texture Analysis** - Instantly scan and list every texture in your prefab with detailed memory metrics
- **📊 Real-Time Size Comparison** - See "Before" vs "After" optimization stats side-by-side in MB/GB
- **⚡ Batch Optimization** - Apply compression settings to multiple textures and audio files simultaneously
- **🎯 Granular Control** - Individual texture/audio selection with resolution, compression format, and mipmap info
- **💾 Memory Tracking** - Track both file size (disk) and runtime memory usage
- **🔧 Mesh Compression** - FBX mesh optimization with vertex/triangle count display
- **📈 Smart Compression** - DXT1/BC1 Crunch compression with adjustable quality (0-100)
- **🎨 Custom Max Size** - Set texture limits: 128, 256, 512, 1024, 2048, 4096
- **🎵 Audio Compression** - Vorbis/ADPCM compression with quality control and sample rate optimization
- **📋 Detailed Logging** - Real-time console output showing every optimization step

**Perfect for:**
- Reducing avatar file sizes by 50-80%
- Meeting VRChat performance rank requirements (Good/Excellent)
- Lowering VRAM usage for better FPS in crowded worlds
- Compressing audio files for smaller builds
- Quick iteration during avatar development

---

### 🎬 **Video Animator** - *The Video Texture Wizard*
Convert videos into optimized Unity texture animations with texture atlas technology:

- **🎥 Video to Texture Atlas** - Automatically converts videos into efficient texture atlases
- **📐 Smart Atlas Packing** - Optimizes frame layout for minimal VRAM usage
- **🎨 Multiple Compression Formats** - PNG with Crunch compression or JPEG with quality control
- **⚡ Custom Shader Integration** - Built-in KSVideoDecoder shader or use your own materials
- **🎞️ Flexible Frame Control** - Adjust resolution, frame rate, and time range
- **📊 VRAM Calculator** - Real-time memory usage estimation
- **🔄 Loop & Audio Support** - Seamless looping with synchronized AudioClip playback
- **📦 Complete Prefab Generation** - Auto-creates prefab, material, animation, and animator controller
- **🎯 Organized Output** - Each video gets its own subfolder: `Assets/Kawaii Studio/Videos/VideoName/`
- **🖼️ Atlas Size Control** - Set maximum atlas dimensions (512-8192px)
- **📹 FFMPEG Integration** - Built-in video processing with preview support

**Perfect for:**
- Creating animated textures for VRChat worlds
- Converting video backgrounds into optimized animations
- Building video screens and displays in Unity
- Reducing video memory footprint with atlas compression
- Syncing video animations with audio tracks

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
   - Check audio clip sample rates, channels, and compression formats
   - Check mesh vertex/triangle counts and current compression settings
   
4. **Configure Optimization**
   - **Texture Settings:**
     - **Max Texture Size:** Choose target resolution (128px to 4096px)
     - **Compression:** Select quality level (Uncompressed/Compressed/High Quality)
     - **Crunch Compression:** Enable for 30-50% additional size reduction
     - **Crunch Quality:** Fine-tune compression (0-100, higher = better quality)
     - **Generate Mipmaps:** Toggle for distance-based LOD
   
   - **Audio Settings:**
     - **Load Type:** Decompress On Load / Compressed In Memory / Streaming
     - **Compression Format:** PCM / Vorbis / ADPCM
     - **Quality:** 1-100 for Vorbis compression
     - **Sample Rate:** Preserve / Override (8000-48000 Hz)
     - **Force To Mono:** Convert stereo to mono for smaller size
   
   - **Mesh Settings:**
     - **Mesh Compression:** Off/Low/Medium/High
     - Automatically enables polygon and vertex optimization
   
5. **Select Assets**
   - Use checkboxes to select individual textures/audio/meshes
   - Or click "Select All" / "Select None" for batch operations
   - Expand categories with ▶/▼ arrows to see full lists
   
6. **Optimize!**
   - Click `🚀 OPTIMIZE` to apply settings instantly
   - Watch the console log for real-time progress
   - See memory savings calculated automatically
   - Review "Size After Opt." column for per-asset results

7. **Review Results**
   - Compare "Original Size" vs "Size After Opt." columns
   - Check percentage saved indicators (e.g., "-67.3%")
   - Read the optimization log for detailed changes
   - Test avatar in VRChat to verify performance improvements

---

### 🎬 Video Animator Workflow

1. **Launch the Tool**
   - Navigate to `Kawaii Studio > Video Animator` in Unity's menu
   
2. **Select Video File**
   - Click `Browse (...)` next to "Video" field
   - Select your video file (MP4, MOV, AVI, WebM, MKV, etc.)
   - Tool automatically analyzes duration, fps, and resolution
   
3. **Configure Settings**
   - **Frame Size:** Set output texture resolution (e.g., 512x512)
   - **Frame Rate:** Adjust playback speed (1-60 fps)
   - **Time Range:** Use slider to trim video start/end points
   - **Audio:** Optionally add an AudioClip for synchronized playback
   
4. **Advanced Settings**
   - **Loop Animation:** Enable seamless video looping
   - **Crunch Compression:** Enable for smaller texture sizes
   - **Use Atlases:** Pack frames into texture atlases (recommended)
   - **Single Atlas:** Force all frames into one texture
   - **Limit Atlas Size:** Set max atlas dimensions (512-8192px)
   - **Save as JPEG:** Use JPEG instead of PNG (smaller but lossy)
   - **Custom Material:** Use your own shader instead of KSVideoDecoder
   
5. **Preview & Convert**
   - Click `Preview` to test settings with FFMPEG player
   - Click `Create Animation` to start conversion
   - Watch progress bar and console log
   - Output saved to `Assets/Kawaii Studio/Videos/VideoName/`
   
6. **Use the Prefab**
   - Drag generated prefab into your scene
   - Video plays automatically with Animator component
   - Audio syncs if AudioClip was provided
   - Adjust scale to match video aspect ratio

**Output Structure:**
```
Assets/Kawaii Studio/Videos/VideoName/
├── VideoName.prefab          (Ready-to-use video quad)
├── VideoName.mat             (Material with textures)
├── VideoName.anim            (Animation clip)
├── VideoName.controller      (Animator controller)
├── VideoName Atlas 0.png     (Texture atlas frames)
├── VideoName Atlas 1.png
└── ...
```

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
![Prefab Optimizer Interface](https://github.com/kawaiistudio/KSUnityTools/blob/main/Documentation~/prefab-optimizer.png)

**What You're Looking At:**
- **Top Section:** Clean purple-themed interface with avatar selection
- **Texture Compression Settings:** Granular control over max size, compression type, crunch settings, and mipmaps
- **Audio Compression Settings:** Load type, compression format, quality, sample rate control
- **Texture List (Collapsible):** Every texture with resolution, original size, optimized size, and percentage saved
- **Audio List (Collapsible):** All audio clips with sample rate, channels, format, and size tracking
- **Mesh Compression Settings:** FBX mesh optimization with compression level selector
- **Mesh List (Collapsible):** All meshes with vertex/triangle counts and individual compression options
- **Optimize Button:** One-click batch processing with real-time counter
- **Console Log:** Live output showing each optimization step with color-coded status messages
- **Memory Tracking:** Real-time display of disk size (MB) and runtime VRAM usage

---

### Video Animator Interface
![Video Animator Interface](https://github.com/kawaiistudio/KSUnityTools/blob/main/Documentation~/Video-Animator.png)

**What You're Looking At:**
- **Video Input:** File browser with automatic video analysis
- **Audio Support:** Optional AudioClip field for synchronized playback
- **Frame Settings:** Resolution and frame rate controls with real-time preview
- **Time Range:** Min/max slider to trim video clips
- **Advanced Settings:** Atlas configuration, compression options, custom materials
- **Statistics Panel:** Real-time VRAM calculation and optimization warnings
- **Output Folder:** Automatic subfolder creation per video
- **Preview & Convert Buttons:** Test settings before final conversion
- **Progress Bar:** Live encoding progress with frame counter
- **Console Log:** Detailed conversion steps and FFMPEG output

**Key Visual Elements:**
- 🟣 Purple headers for section organization
- 🔴 Red action buttons for primary operations
- 🟢 Green success indicators and statistics
- ⚫ Dark console background for readability
- Organized folder structure display

---

### GLB to FBX Converter Interface
![GLB Converter Interface](https://github.com/kawaiistudio/KSUnityTools/blob/main/Documentation~/glb-converter.png)

**What You're Looking At:**
- **Blender Path Section:** Auto-detection button with manual browse fallback
- **GLB File Input:** Simple file picker for source model
- **Output Folder Selector:** Choose where to save converted files
- **Convert Button:** Large, prominent action button with emoji indicator
- **Status Indicator:** Real-time conversion status (READY/Converting)
- **Console Output:** Live streaming of Blender's conversion process
- **Clean UI:** Minimal distractions, maximum functionality

---

## 🛠️ System Requirements

| Component | Requirement |
|-----------|-------------|
| **Unity** | 2019.4 or newer |
| **VRChat SDK** | 3.0 or later (for Prefab Optimizer) |
| **Blender** | 2.8+ (for GLB Converter) |
| **FFMPEG** | Latest version (for Video Animator) |
| **OS** | Windows, macOS, or Linux |
| **.NET** | 4.x or later |

---

## 📝 Changelog

### v2.0 (Current Release)
- ✨ **NEW:** Video Animator tool with texture atlas generation
- ✨ **NEW:** Audio compression optimizer with Vorbis/ADPCM support
- 🎨 Improved UI consistency across all tools
- 📊 Enhanced memory tracking with VRAM calculations
- 🔧 Better FFMPEG integration for video processing
- 📁 Automatic subfolder organization for videos
- 🐛 Fixed audio import parameter bugs
- 🏷️ Updated version labels to v2.0

### v1.1
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
- ☕ [Buy me a coffee](https://ko-fi.com/laylakitsune) *(optional)*

---

<div align="center">

<img src="https://github.com/kawaiistudio/KSUnityTools/blob/main/logo_v2.png" alt="Kawaii Studio Logo" width="120"/>

### ⚡ Made with 💜 by Kawaii Studio

*Empowering VRChat creators with professional-grade tools*

[GitHub](https://github.com/kawaiistudio) • [VRChat Group](https://vrchat.com/home/group/grp_7bf987ee-2f4a-4eae-b9b5-c060b97250ab) • [Discord](https://discord.gg/xAeJrSAgqG) • [Telegram](https://t.me/kawaiistudio)

</div>
