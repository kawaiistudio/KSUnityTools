# ⚡ KSUnityTools

**Professional Unity optimization and conversion tools by Kawaii Studio**

![Unity](https://img.shields.io/badge/Unity-2019.4%2B-blueviolet?style=for-the-badge&logo=unity)
![License](https://img.shields.io/badge/License-MIT-success?style=for-the-badge)
![VRChat](https://img.shields.io/badge/VRChat-Ready-ff4757?style=for-the-badge)

A collection of powerful, easy-to-use Unity Editor tools designed for VRChat creators and professional Unity developers. Optimize your prefabs and convert 3D models with just a few clicks!

---

## 🎯 Features

### 🔧 Prefab Optimizer v1.1
Compress and optimize your Unity prefabs with advanced texture and mesh compression.

**Key Features:**
- 🎨 **Texture Compression**
  - Automatic texture size optimization
  - Crunch compression support
  - Mipmap generation control
  - Real-time memory size tracking (shows actual Unity memory usage)
  - Before/After comparison with savings percentage
  
- 🔧 **Mesh Compression**
  - High/Medium/Low/Off compression levels
  - FBX mesh optimization
  - Polygon and vertex optimization
  - Individual compression control per mesh

- 📊 **Advanced Analytics**
  - Original vs Optimized size comparison
  - Memory usage tracking (RAM)
  - Detailed optimization logs
  - Per-asset compression preview

### 🔄 GLB to FBX Converter
Convert GLB/GLTF files to FBX format with automatic material setup and texture extraction.

**Key Features:**
- 🚀 **One-Click Conversion**
  - GLB → FBX conversion via Blender
  - Automatic Blender detection
  - Material and texture preservation
  
- 🎨 **Smart Material Processing**
  - Automatic PBR material setup
  - Base Color, Normal, Metallic/Roughness support
  - Emissive texture handling
  - Textures automatically extracted and organized

- 📁 **Organized Output**
  - Clean folder structure
  - Separated texture directory
  - Embedded textures in FBX

---

## 📦 Installation

### Method 1: Unity Package Manager (Recommended)
1. Open Unity
2. Go to `Window > Package Manager`
3. Click `+` → `Add package from git URL`
4. Paste: `https://github.com/yourusername/KSUnityTools.git`

### Method 2: Manual Installation
1. Download the latest release from [Releases](https://github.com/kawaiistudio/KSUnityTools/releases)
2. Extract the files
3. Copy the `KawaiiStudio` folder to your Unity project's `Assets` folder

---

## 🎮 Usage

### Prefab Optimizer

1. **Open the Tool**
   - Go to `Kawaii Studio > Prefab Optimizer` in Unity menu

2. **Select Your Prefab**
   - Drag and drop your prefab into the "Drag Prefab Here" field

3. **Scan the Prefab**
   - Click `🔍 SCAN PREFAB` to analyze textures and meshes

4. **Configure Settings**
   - **Textures:** Set max size, compression quality, crunch settings
   - **Meshes:** Choose compression level (High/Medium/Low/Off)

5. **Select Assets**
   - Use checkboxes to select which textures/meshes to optimize
   - Or use `Select All` / `Select None` buttons

6. **Optimize!**
   - Click `🚀 OPTIMIZE` button
   - View real-time memory savings in the log

**Pro Tips:**
- Hover over textures to see detailed compression info
- "Original Size" shows file size on disk
- "Size After Opt." shows actual Unity memory usage
- Green percentage shows how much memory you saved!

---

### GLB to FBX Converter

1. **Open the Tool**
   - Go to `Kawaii Studio > GLB to FBX Converter` in Unity menu

2. **Set Up Blender**
   - The tool will auto-detect Blender on first launch
   - If not found, click `Auto-Detect` or `Browse` to select manually

3. **Select Your Files**
   - **GLB File:** Choose the .glb file you want to convert
   - **Output Folder:** Select where to save the converted files

4. **Convert**
   - Click `🚀 CONVERT GLB TO FBX`
   - Watch the console for progress
   - Files will be organized in a new folder with textures separated

**Requirements:**
- Blender 2.8+ must be installed
- Blender path is saved for future use

---

## 🎯 Perfect For

- ✅ **VRChat Creators** - Optimize avatars and worlds for better performance
- ✅ **Game Developers** - Reduce memory usage and improve load times
- ✅ **3D Artists** - Quick model format conversion with material preservation
- ✅ **Unity Beginners** - Easy-to-use interface with visual feedback
- ✅ **Professional Studios** - Batch optimization and quality control

---

## 📊 Before & After Examples

### Texture Optimization
```
Before: 2048x2048 PNG (12.3 MB on disk, 15.82 KB in memory)
After:  128x128 Compressed (0.6 MB in memory)
Savings: 96% memory reduction!
```

### Mesh Optimization
```
Before: 50,000 vertices, No compression
After:  50,000 vertices, High compression
Result: Smaller file size, same visual quality
```

---

## 🛠️ System Requirements

- **Unity:** 2019.4 or later
- **OS:** Windows, macOS, Linux
- **Blender:** 2.8+ (for GLB Converter only)
- **.NET:** 4.x or later

---

## 🎨 Screenshots

### Prefab Optimizer
![Prefab Optimizer Interface](screenshots/prefab-optimizer.png)
*Clean, modern interface with real-time memory tracking*

### GLB to FBX Converter
![GLB Converter Interface](screenshots/glb-converter.png)
*Automatic Blender detection and one-click conversion*

---

## 🤝 Contributing

We welcome contributions! Here's how you can help:

1. 🍴 Fork the repository
2. 🌟 Create a feature branch (`git checkout -b feature/AmazingFeature`)
3. 💾 Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. 📤 Push to the branch (`git push origin feature/AmazingFeature`)
5. 🎉 Open a Pull Request

---

## 📝 Changelog

### v1.1.0 (Current)
- ✨ Added memory size tracking (shows actual Unity RAM usage)
- 🎨 Improved UI with before/after comparison
- 📊 Percentage savings calculation
- 🔧 Per-mesh compression control
- 🐛 Bug fixes and performance improvements

### v1.0.0
- 🎉 Initial release
- 🔧 Prefab Optimizer
- 🔄 GLB to FBX Converter

---

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

```
MIT License

Copyright (c) 2024 Kawaii Studio

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.
```

---

## 💬 Support & Community

- 📧 **Email:** support@kawaiistudio.dev
- 💬 **Discord:** [Join our server](https://discord.gg/xAeJrSAgqG)
- 🐛 **Bug Reports:** [GitHub Issues](https://github.com/kawaiistudio/KSUnityTools)
- 📖 **Documentation:** [Full Docs](https://github.com/kawaiistudio/KSUnityTools)

---

## ⭐ Show Your Support

If you find these tools helpful, please consider:
- ⭐ Starring this repository
- 🐦 Sharing with your friends
- 💖 Supporting our work

---

## 🙏 Acknowledgments

- Unity Technologies for the amazing game engine
- Blender Foundation for the open-source 3D software
- VRChat community for inspiration and feedback
- All contributors and users who help improve these tools

---

<p align="center">
  <strong>Made with 💜 by Kawaii Studio</strong>
  <br>
  <sub>Open Source • Free Forever • Community Driven</sub>
</p>

<p align="center">
  <a href="#-features">Features</a> •
  <a href="#-installation">Installation</a> •
  <a href="#-usage">Usage</a> •
  <a href="#-contributing">Contributing</a> •
  <a href="#-license">License</a>
</p>
