# Tailwind CSS for Visual Studio

![version](https://vsmarketplacebadges.dev/version-short/TheronWang.TailwindCSSIntellisense.jpg)
![installs](https://vsmarketplacebadges.dev/installs-short/TheronWang.TailwindCSSIntellisense.jpg)

Bring IntelliSense, linting, class sorting, build tools, and more to Tailwind CSS in Visual Studio 2026 and 2022.

> **Note**: This extension best supports Tailwind CSS v3+.

**[Download from the Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=TheronWang.TailwindCSSIntellisense)** · **[Getting Started Guide](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/blob/main/Getting-Started.md)** · **[Changelog](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/blob/main/CHANGELOG.md)**

---

## ❤️ Support This Project

This extension is built and maintained solo, in my free time, for the whole VS community.

If it's saved you time or made your workflow better, please consider **[sponsoring development on GitHub](https://github.com/sponsors/theron-wang)** — even a small donation helps keep it going and directly funds new features and bug fixes.

---

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
- [Disclaimer](#disclaimer)

## Prerequisites

This extension uses `npm` and `node`, so you should have them installed.

To check whether `npm` is installed, run `npm -v` in the terminal.

If you don't have `npm` installed, follow the [official install guide](https://docs.npmjs.com/downloading-and-installing-node-js-and-npm) from the npm docs.

## Setup

The extension activates automatically when:
- Your solution contains a `tailwind.config.{js,cjs,mjs,ts,cts,mts}` file (Tailwind v3), or
- You're using Tailwind v4 and importing it in a `.css` file with `@import "tailwindcss"`

If the config file isn't detected automatically, right-click it in Solution Explorer and select **Set as configuration file**.

## Features

### IntelliSense

Get Tailwind class suggestions in Razor, HTML, and CSS files:

<p align="center">
  <img src="https://raw.githubusercontent.com/theron-wang/Tailwind-CSS-for-Visual-Studio/main/art/IntelliSense-Demo-1.gif" width="700" alt="IntelliSense suggesting Tailwind classes in a Razor file" />
</p>

### Linting

Automatically flags:
- Conflicting classes
- Invalid `theme()`, `screen()`, or `@tailwind` usage

> **Note**: Visual Studio may still flag some Tailwind features like `@apply` as errors — extensions can't override these built-in warnings.

<p align="center">
  <img src="https://raw.githubusercontent.com/theron-wang/Tailwind-CSS-for-Visual-Studio/main/art/Linter.png" width="700" alt="Linter flagging conflicting Tailwind classes" />
</p>

### Class Sorting

Sort Tailwind classes:
- Automatically on save or build
- Manually from the **Tools** menu

<p align="center">
  <img src="https://raw.githubusercontent.com/theron-wang/Tailwind-CSS-for-Visual-Studio/main/art/class-sort-demo.gif" width="700" alt="Classes being automatically sorted on save" />
</p>

<table align="center">
<tr>
<td align="center"><img src="https://raw.githubusercontent.com/theron-wang/Tailwind-CSS-for-Visual-Studio/main/art/Class-Sort-2.png" width="380" alt="Manual class sort from the Tools menu" /></td>
</tr>
</table>

### Build Integration

The extension can build your Tailwind CSS output automatically on project build, or manually from the **Build** menu.

- Make sure your input and output CSS files are defined
- Output and errors appear in the Build output window

<table align="center">
<tr>
<td align="center"><img src="https://raw.githubusercontent.com/theron-wang/Tailwind-CSS-for-Visual-Studio/main/art/Build-Demo-1.png" width="380" alt="Build integration output" /></td>
<td align="center"><img src="https://raw.githubusercontent.com/theron-wang/Tailwind-CSS-for-Visual-Studio/main/art/Build-Demo-2.png" width="380" alt="Build menu with Tailwind build option" /></td>
</tr>
</table>

To configure build and configuration files, right-click `.js`, `.ts`, or `.css` files:

<table align="center">
<tr>
<td align="center"><img src="https://raw.githubusercontent.com/theron-wang/Tailwind-CSS-for-Visual-Studio/main/art/Customizability-Build-1.png" width="380" alt="Setting the configuration file via right-click" /></td>
<td align="center"><img src="https://raw.githubusercontent.com/theron-wang/Tailwind-CSS-for-Visual-Studio/main/art/Customizability-Build-2.png" width="380" alt="Configuring build settings via right-click" /></td>
</tr>
</table>

Project-specific settings are saved in a `tailwind.extension.json` file in your project root.

### NPM Integration

Start quickly by right-clicking your project and selecting a startup task:

<p align="center">
  <img src="https://raw.githubusercontent.com/theron-wang/Tailwind-CSS-for-Visual-Studio/main/art/NPM-Shortcuts-1.png" width="700" alt="NPM startup task shortcuts in the right-click menu" />
</p>

**Using the Tailwind CLI?**
1. Set its path under **Tools > Options > Tailwind CSS IntelliSense > Tailwind CLI path**
2. Click **Set up Tailwind CSS (use CLI)**

**Want to use a custom build script?**
1. Define it in your `package.json`
2. Set the script name in the extension options (`npm run your-script-name`)

### Extension Options

Global extension settings live under:

> **Tools > Options > Tailwind CSS IntelliSense**

See [Getting Started – Extension Configuration](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/blob/main/Getting-Started.md#extension-configuration) for details.

## Troubleshooting

### Build Issues

If your CSS isn't updating, check the **Build output** window for Tailwind errors.

<p align="center">
  <img src="https://raw.githubusercontent.com/theron-wang/Tailwind-CSS-for-Visual-Studio/main/art/Troubleshooting-Build.png" width="700" alt="Build output window showing a Tailwind error" />
</p>

### Extension Issues

If the extension crashes or behaves unexpectedly, check the **Extensions** output window for detailed logs.

## Support & Feedback

Found a bug or have a feature request? [Open an issue on GitHub](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/new).

Enjoying the extension? A rating on the [Marketplace listing](https://marketplace.visualstudio.com/items?itemName=TheronWang.TailwindCSSIntellisense) or a **[donation](https://github.com/sponsors/theron-wang)** goes a long way toward keeping it maintained. 🙏

## Disclaimer

This is **not** an official Tailwind CSS extension and has **no affiliation** with Tailwind Labs Inc.
