# Tailwind CSS for Visual Studio

![version](https://vsmarketplacebadges.dev/version-short/TheronWang.TailwindCSSIntellisense.jpg)
![installs](https://vsmarketplacebadges.dev/installs-short/TheronWang.TailwindCSSIntellisense.jpg)

Bring IntelliSense, linting, class sorting, build tools, and more to Tailwind CSS in Visual Studio 2026 and 2022.

> **Note**: This extension best supports Tailwind CSS v3+.

**[Download from the Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=TheronWang.TailwindCSSIntellisense)** · **[Getting Started Guide](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/blob/main/Getting-Started.md)** · **[Changelog](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/blob/main/CHANGELOG.md)**

## ❤️ Support This Project

This extension is built and maintained solo, in my free time, for the whole VS community.

If it's saved you time or made your workflow better, please consider **[sponsoring development on GitHub](https://github.com/sponsors/theron-wang)** — even a small donation helps keep it going and directly funds new features and bug fixes.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Setup](#setup)
- [Features](#features)
  - [IntelliSense](#intellisense)
  - [Linting](#linting)
  - [Class Sorting](#class-sorting)
  - [Build Integration](#build-integration)
  - [NPM Integration](#npm-integration)
  - [Extension Options](#extension-options)
- [Troubleshooting](#troubleshooting)
- [Support & Feedback](#support--feedback)
- [Development](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/blob/main/DEVELOPERS.md)
- [Disclaimer](#disclaimer)

## Prerequisites

This extension uses `npm` and `node`, so you should have them installed.

To check whether `npm` is installed, run `npm -v` in the terminal.

If you don't have `npm` installed, follow the [official install guide](https://docs.npmjs.com/downloading-and-installing-node-js-and-npm) from the npm docs.

## Setup

The extension activates automatically when:
- Your solution contains a `tailwind.config.{js,cjs,mjs,ts,cts,mts}` file (Tailwind v3), or
- You're using Tailwind v4 and importing it in a `.css` file with `@import "tailwindcss"`

If the config file isn't detected automatically, right-click it in Solution Explorer and select **Add Tailwind CSS configuration file**.

## Features

### IntelliSense

Get Tailwind class suggestions in Razor, HTML, and CSS files, including custom theme values. Color previews and CSS descriptions help you choose the right utility:

<p align="center">
  <img src="https://raw.githubusercontent.com/theron-wang/Tailwind-CSS-for-Visual-Studio/main/art/readme/intellisense.gif" width="800" alt="Live IntelliSense while typing rounded-lg and bg-sky- in a Razor file, with color suggestions and a CSS preview before accepting bg-sky-500" />
</p>

[View the still IntelliSense screenshot](https://raw.githubusercontent.com/theron-wang/Tailwind-CSS-for-Visual-Studio/main/art/readme/intellisense.png).

### Linting

Automatically flags:
- Conflicting classes
- Invalid `theme()`, `screen()`, or `@tailwind` usage

> **Note**: Visual Studio may still flag some Tailwind features like `@apply` as errors — extensions can't override these built-in warnings.

<p align="center">
  <img src="https://raw.githubusercontent.com/theron-wang/Tailwind-CSS-for-Visual-Studio/main/art/readme/linting.png" width="800" alt="The linter underlining px-4 and px-6 and explaining that both classes set the same CSS properties" />
</p>

### Class Sorting

Sort Tailwind classes:
- Automatically on save or build
- Manually from the **Tools** menu

<p align="center">
  <img src="https://raw.githubusercontent.com/theron-wang/Tailwind-CSS-for-Visual-Studio/main/art/readme/class-sorting.gif" width="800" alt="Saving a Razor file sorts py-2 rounded-lg px-4 bg-sky-500 into rounded-lg bg-sky-500 px-4 py-2" />
</p>

With Tailwind CSS v4.2 or later, the **Tools** menu also offers **Canonicalize Tailwind classes** commands to replace utilities with their recommended equivalents. You can configure canonicalization to run on save or build in the extension options.

### Build Integration

The extension can build your Tailwind CSS output automatically on project build, or manually from the **Build** menu.

- Make sure your input and output CSS files are defined
- Output and errors appear in the Build output window

<p align="center">
  <img src="https://raw.githubusercontent.com/theron-wang/Tailwind-CSS-for-Visual-Studio/main/art/readme/build-integration.png" width="800" alt="Tailwind build, minify, and unminify commands in the Build menu" />
</p>

<p align="center">
  <img src="https://raw.githubusercontent.com/theron-wang/Tailwind-CSS-for-Visual-Studio/main/art/readme/build-output.png" width="800" alt="The Build output window reporting a successful Tailwind CSS build" />
</p>

To configure files, right-click them in Solution Explorer. Use **Add Tailwind CSS configuration file** for your configuration, **Add Tailwind CSS input file** for your input CSS, and **Set as Tailwind CSS output file** for the output.

Project-specific settings are saved in a `tailwind.extension.json` file in your project root.

### NPM Integration

To set up a project, right-click it in Solution Explorer and choose **Set up Tailwind CSS > Set up and install in project**. You can also use an existing global NPM installation:

<p align="center">
  <img src="https://raw.githubusercontent.com/theron-wang/Tailwind-CSS-for-Visual-Studio/main/art/readme/npm-integration.png" width="800" alt="The Set up Tailwind CSS submenu offering a project installation or an existing global NPM installation" />
</p>

**Using the Tailwind CLI?**
1. Set its path under **Tools > Options > Tailwind CSS IntelliSense > Tailwind CLI path**
2. Right-click your project and choose **Set up Tailwind CSS > Set up using CLI standalone**

**Want to use a custom build script?**
1. Define it in your `package.json`
2. Set **Build script** in the extension options to the script's name (for example, `build:css`)

### Extension Options

Global extension settings live under:

> **Tools > Options > Tailwind CSS IntelliSense**

<p align="center">
  <img src="https://raw.githubusercontent.com/theron-wang/Tailwind-CSS-for-Visual-Studio/main/art/readme/extension-options.png" width="800" alt="General extension options showing build behavior, class canonicalization, sorting on save, and completion priority" />
</p>

See [Getting Started – Extension Configuration](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/blob/main/Getting-Started.md#extension-configuration) for details.

## Troubleshooting

### Build Issues

If your CSS isn't updating, check the **Build output** window for Tailwind errors.

### Extension Issues

If the extension crashes or behaves unexpectedly, check the **Extensions** output window for detailed logs.

## Support & Feedback

Found a bug or have a feature request? [Open an issue on GitHub](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/new).

Enjoying the extension? A rating on the [Marketplace listing](https://marketplace.visualstudio.com/items?itemName=TheronWang.TailwindCSSIntellisense) or a **[donation](https://github.com/sponsors/theron-wang)** goes a long way toward keeping it maintained. 🙏

## Disclaimer

This is **not** an official Tailwind CSS extension and has **no affiliation** with Tailwind Labs Inc.
