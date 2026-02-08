# The-FuseStreet 融街
A Gemini 3 Pro + Nano Banana Pro‑powered AI tool for rapid game art/cultural design test‑fit.

**AI-Powered Cultural Fusion Platformer**

A 2D side-scrolling platformer where players explore worlds born from cultural fusion. Enter two cultures (e.g., "Nordic Cyberpunk" + "Ancient Egyptian"), and AI generates the entire visual experience — background, character sprites, and platform textures — in real-time.

### ✨ Key Features

- **AI Visual Generation**: Gemini 3 Pro generates art direction prompts; image AI creates spritesheets on pure white backgrounds
- **Dynamic Frame Extraction**: Custom algorithm detects character regions by scanning pixel density and finding gaps — no fixed grid needed
- **GLSL Chroma Key Shader**: Real-time background removal with edge erosion to eliminate white fringing
- **Fully Automated Pipeline**: From text input to playable animated character, zero manual sprite editing

### 🛠️ Tech Stack

- **Engine**: Godot with C# + GLSL
- **AI APIs**: Google Gemini 3 Pro (text), Gemini 3 Pro Image (visuals)
- **Shader**: Custom GLSL chroma key with 8-neighbor edge erosion

### 🎮 How It Works

1. User inputs culture description
2. Gemini generates 3 image prompts (background, player, platform)
3. Image AI creates spritesheet (4 idle + 6 run frames)
4. Dynamic algorithm extracts frames by detecting content density gaps
5. Shader removes white background in real-time
6. Character runs and jumps!

**AI驱动的文化融合平台跳跃游戏**

一款2D横版平台跳跃游戏，玩家探索由文化融合诞生的世界。输入两种文化（如"北欧赛博朋克" + "古埃及"），AI实时生成完整视觉体验 —— 背景、角色精灵图、平台纹理。

### ✨ 核心特性

- **AI视觉生成**：Gemini 3 Pro生成美术指导提示词，图像AI在纯白背景上创建精灵图
- **动态帧提取**：自研算法通过扫描像素密度、寻找间隙来检测角色区域 —— 无需固定网格
- **GLSL色度键着色器**：实时背景移除 + 边缘侵蚀消除白边
- **全自动流水线**：从文字输入到可玩动画角色，零手动切图

### 🛠️ 技术栈

- **引擎**：Godot + C# + GLSL
- **AI接口**：Google Gemini 3 Pro（文本）、Gemini 3 Pro Image（图像）
- **着色器**：自定义GLSL色度键 + 8邻居边缘侵蚀

### 🎮 工作原理

1. 用户输入文化描述
2. Gemini生成3个图像提示词（背景、角色、平台）
3. 图像AI创建精灵图（4帧待机 + 6帧奔跑）
4. 动态算法通过检测内容密度间隙提取帧
5. 着色器实时移除白色背景
6. 角色跑起来了！


![Screenshot-1](FinalUIAndVisuals/hhhh.png)
