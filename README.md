# MC Offline Chat

<p align="center">
<img src="Docs/Images/Logo.png" width="180"/>

# Private • Offline • AI Powered
### A Local-First AI Assistant for Creativity, Writing, Research and Development

_No subscriptions. No cloud dependency. Your AI. Your models. Your data._

---

## Overview

MC Offline Chat is an offline-first Artificial Intelligence assistant built using **.NET MAUI** that allows users to run local GGUF Large Language Models directly on supported desktop and mobile devices.

Unlike traditional cloud AI assistants, MC Offline Chat is designed around a simple philosophy:

> **Your conversations, documents, stories and creative work belong to you—not to a server.**

The application provides a modern offline chat experience together with document editing, world building, character creation, creative writing tools and local AI model management.

MC Offline Chat originated from technology developed for the SyntheticAI platform before being redesigned into a lightweight standalone AI application focused entirely on privacy and offline capability.

---

# Philosophy

MC Offline Chat follows five core principles.

## Offline First

The application is designed to function without an Internet connection.

Internet connectivity is optional and only used for features such as:

- Git Repository Search
- Optional model downloads
- Future plugin services

All core AI functionality remains available offline.

---

## Privacy First

No conversations are uploaded.

No prompts are stored online.

No telemetry leaves the device.

All conversations and creative projects remain stored locally.

---

## User Owned Models

Users choose which GGUF model they wish to use.

The application does not lock users into a single AI provider.

Supported model sizes depend entirely on the user's hardware.

Examples include:

- Tiny Draft Models
- 1B Models
- 2B Models
- 3B Models
- 4B Models
- Future Mobile Optimized Models

---

## Modular Architecture

The application is built around independent modules.

Examples include:

- Chat
- Stories
- Characters
- Document Editor
- Git Search
- Telemetry
- Settings
- Model Manager

Each module is designed to evolve independently.

---

## Local Data Ownership

Everything created by the user remains on the device.

Including:

- Stories
- Characters
- Conversations
- Documents
- Images
- Metadata
- Prompt Templates
- Settings

---

# Features

## Offline AI Chat

- Local GGUF inference
- Multiple model support
- Conversation history
- Chat sessions
- Session restore
- Conversation summaries
- Local prompt management
- Streaming responses (where supported)
- Heuristic fallbacks

---

## Story Studio

Create persistent story worlds.

Each world contains structured information including:

- World Name
- Story Description
- Timeline
- Terrain
- World Rules
- Magic System
- Tags
- Inhabitants
- Images
- Previous Chapters
- Metadata

Stories remain saved locally.

---

## Character Studio

Design reusable characters.

Each character stores:

- Name
- Surname
- Species
- Age
- Personality
- Description
- Appearance
- Clothing
- Inventory
- Abilities
- Likes
- Dislikes
- Fears
- Starting Location
- Avatar Image

Characters can be reused across multiple stories.

---

## Document Editor

Built-in document editor supporting:

- C#
- C++
- JSON
- XML
- Markdown
- JavaScript
- TypeScript
- HTML
- CSS
- Android XML
- YAML
- Plain Text

Features include:

- Open
- Edit
- Save
- Save As
- Recent Documents
- Local Storage

---

## Git Search

Integrated repository search system.

Allows searching public repositories using keywords.

Useful for:

- Learning
- Research
- Examples
- Documentation
- Code Discovery

---

## Dashboard

The dashboard provides a quick overview including:

- Current Model
- Recent Conversations
- Last Story
- Last Character
- Recent Documents
- AI Status
- Storage Information

---

## History

View previously created:

- Chats
- Stories
- Characters
- Documents

History allows reopening previous work without losing context.

---

## Model Manager

Manage local AI models.

Functions include:

- Import GGUF
- Remove Models
- Select Active Model
- View Model Information
- Storage Usage
- Capability Detection

---

## Text To Speech

Optional local text-to-speech support.

Allows AI responses to be spoken aloud.

---

## Telemetry

Local-only application telemetry.

Includes:

- Startup Times
- Model Load Times
- Memory Usage
- Performance Metrics
- Diagnostics

No telemetry is transmitted externally.

---

# Architecture

```
MC Offline Chat

├── Presentation
│
├── Chat Engine
│
├── Story Engine
│
├── Character Engine
│
├── Document Manager
│
├── Conversation History
│
├── GGUF Runtime
│
├── Model Manager
│
├── Git Search
│
├── Telemetry
│
├── Local Storage
│
└── Settings
```

---

# Story Engine

Stories maintain persistent metadata.

Each AI prompt includes relevant story context such as:

- World Definition
- Character Metadata
- Previous Chapters
- Active Rules
- Timeline
- Inventory
- Current Objectives

This helps reduce story drift and maintain consistency across long conversations.

---

# Character Memory

Characters persist independently from conversations.

Each character tracks:

- Equipment
- Inventory Changes
- Relationships
- Personality
- Abilities
- Biography

Characters may be reused across different stories.

---

# Local Storage

MC Offline Chat stores data locally.

Examples include:

```
Stories/

Characters/

Chats/

Documents/

Images/

Models/

Settings/

Telemetry/
```

No cloud storage is required.

---

# Technology Stack

- .NET MAUI
- C#
- MVVM
- SQLite
- Local File Storage
- GGUF
- LLamaSharp
- System.Text.Json

---

# Current Goals

- Stable Offline AI
- Fast Startup
- Efficient Memory Usage
- Clean UI
- Expandable Plugin System
- Better Mobile Performance

---

# Planned Features

- Semantic Search
- AI Memory Compression
- Prompt Templates
- Workspace Projects
- Markdown Export
- PDF Export
- Local Vector Search
- Plugin SDK
- Voice Conversations
- Multi-character Sessions

---

# Supported Platforms

- Android
- Windows
- Linux (future)
- macOS (future)
- iOS (future)

---

# Development Philosophy

MC Offline Chat focuses on practical engineering rather than unnecessary complexity.

The project aims to remain:

- Lightweight
- Offline
- Fast
- Private
- Modular
- Extensible

---

# Contributing

Contributions, bug reports and feature suggestions are welcome.

Please open an issue before submitting large changes.

---

# License

License to be determined.

---

# Acknowledgements

Built using the .NET ecosystem together with open AI technologies including GGUF-compatible language models.

Special thanks to the open-source AI community whose work has made local AI practical on consumer hardware.

---

# MC Offline Chat

**Private AI. Local AI. Your AI.**
