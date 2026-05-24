# Changelog

## 1.13.10 (May 24th, 2026)

- Fix a bug where class lists with single quotes in arbitrary classes (i.e., `before:content-['']`) would cause an error
- Fix performance issues on startup

## 1.13.9 (May 22nd, 2026)

- Add arbitrary variants into quick info tooltips (i.e., `[&_td]` becomes `& td` in the tooltip) ([#142](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/142))
- Remove hex equivalents in descriptions for colors that are outside the RGB gamut

## 1.13.8 (May 21st, 2026)

- Fix incorrect color descriptions for oklch colors out of the normal RGB space ([#142](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/142))

## 1.13.7 (May 15th, 2026)

- Add support for Tailwind v4.3
- Use a diff-based system for Tailwind resources to minimize extension size

## 1.13.6 (April 23rd, 2026)

- `tailwind.extension.json` should be generated in the project the user right-clicks to set up Tailwind in
- Do not save to `ConfigurationFiles` in v4 projects as expected
- Support CSS plugins using `@import` ([#139](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/139))

## 1.13.5 (April 18th, 2026)

- Correctly match best configuration file (based on content paths) to the file being viewed
- Allow `prefix()` to be placed in the same `@import "tailwindcss"` line as `source()`
- Automatically add prefix (if specified) when tabbing the completion menu

## 1.13.4.1 (April 13th, 2026)

- Update extension name from `Tailwind CSS VS2022 Editor Support` to `Tailwind CSS for Visual Studio` ([#134](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/134))

## 1.13.4 (February 28th, 2026)

- Add support for v4.2
- Fix missing `caret-*` and `placeholder-*` classes
- Fix automatic updater
- Remove unnecessary DLLs

## 1.13.3 (January 25th, 2026)

- Fix missing utilities for custom colors from plugins ([#133](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/133))

## 1.13.2 (January 17th, 2026)

- Fix performance and reliability issues with initial `@plugin` support ([#133](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/133))
- Fix missing descriptions for plugin classes
- Fix incorrect CSS syntax in quick info tooltips

## 1.13.1 (January 12th, 2026)

- Allow `{` after `@plugin` in addition to `;`

## 1.13.0 (January 12th, 2026)

- Re-add support for legacy JavaScript plugins in v4 projects ([#133](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/133))

## 1.12.9 (December 2nd, 2025)

- Revert `-c` removal from 1.12.6
- Improve verbose build logs to include working directory

## 1.12.8 (November 29th, 2025)

- Fix the space before `!important` being removed when sorting classes ([#132](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/132))

## 1.12.7 (November 28th, 2025)

- Fix incorrect result when tabbing through completion menu when a modifier and class are added in the same session
- Fix additional `tailwind.extension.json` file being added to solution root

## 1.12.6 (November 28th, 2025)

- Run build processes in the directory of the input file, not of the configuration file (affects v3 only) --> remove use of `-c` to let Tailwind find the correct config file ([#131](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/131))
- Use closest configuration file instead of default configuration file when a file is not explicitly included in a configuration file
- Moved additional solution startup code to the background to improve performance

## 1.12.5 (November 26th, 2025)

- Fix incorrect display of `Set up Tailwind CSS` command in Solution Explorer context menu when Tailwind is already installed correctly
- Solution startup performance optimizations ([#130](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/130))

## 1.12.4 (October 20th, 2025)

- Fix `tailwind.extension.json` being deleted in v3 projects ([#129](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/129))
- Fix some parsing errors in JS Tailwind plugins
- Fix missing description tooltips for completions when using prefixes in v3 projects

## 1.12.3 (August 17th, 2025)

- Fix `PSSecurityException` by running `npx @tailwindcss/cli` with `-ExecutionPolicy Unrestricted` if powershell has a security restriction ([#120](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/120), [#125](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/125), see also [npm/cli#7280](https://github.com/npm/cli/issues/7280))
- If a project uses the Tailwind standalone CLI, find the version of that instead of trying to find a local/global npm tailwind installation
- Fix missing color tooltips for `rgb` with commas
- Fix incorrect color description if changing its value in a configuration file
- Fix Tailwind CSS updating with npm even if a standalone CLI is configured

## 1.12.2 (July 28th, 2025)

- Make razor class sorting respect indents and newlines (based on token count, not character count)

## 1.12.1 (July 12th, 2025)

- Add completions for certain missing classes (`max-w-{fraction}`, `min-w-{fraction}`, `mask-{...}-{from|to}-{number or percent}`)

## 1.12.0 (July 7th, 2025)

- Add completion support for `@theme` variables
- Fix missing descriptions for classes using arbitrary values containing spacing units (i.e. `font-[inherit]!` contains `in`)

## 1.11.4 (July 6th, 2025)

- Fix potential NREs when updating Tailwind on project load ([#119](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/119))

## 1.11.3 (July 3rd, 2025)

- Add missing `bg-gradient-to-*` classes
- Properly sort `group-`, `peer-`, breakpoints after all other variants in v3 projects
- Fix incorrect detection of configuration files if they contain a newline before `@import "tailwindcss"`
- Fix certain descriptions not showing up for custom classes
- Add description support for `--text-*--line-height`, `--text-*--font-weight`, and `--text-*--letter-spacing`

## 1.11.2 (June 26th, 2025)

- Show powershell build command in build output when verbose build is toggled
- Show `Set up Tailwind CSS` command in the Solution Explorer context menu when no installation is found (either locally or globally) and no valid standalone CLI configuration is provided, regardless of whether configuration files have already been defined via `tailwind.extension.json`
- Fix an error which could occur when unloading and reloading projects while having a file open
- Fix error "`Identifier 'configuration' has already been declared` in parser.js" ([#117](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/117))

## 1.11.1.1 (June 9th, 2025)

- Fix razor parsing regex to correctly match `bg-(--var)` Tailwind classes
- Fix razor parsing regex to correctly match complex classes like `text-@(error ? "red-700" : "green-900")`

## 1.11.1 (June 9th, 2025)

- Add missing utilities for all color classes: (`{utility}-inherit`, `{utility}-current`, `{utility}-transparent`)
- Put variable values/comments directly after they are referenced in class descriptions
- Prevent all classes from generating (though hidden) in the completion menu when using a modifier on `text-` and bg gradient classes
- Fix descriptions being truncated when there is more css after a closing brace

## 1.11.0 (June 8th, 2025)

- Add support for breakpoints and containers
- Fix razor parsing for functions with the ternary conditional operator ([#116](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/116))
- Fix razor parsing for functions with string parameters (i.e. `@Foo("bar")`)
- Properly handle `@@` or `@("@")` escaping in razor, for classes like `@@sm:bg-red-500`
- Fix an error in v3 projects with a configuration file with no `content` property
- Fix `.ts` file support for v3 projects

## 1.10.14 (June 6th, 2025)

- Handle v4 prefixes that go in the very beginning, separated with a colon
- Bug fixes for classes with the important modifier
- Properly handle arbitrary classes with parenthesis (i.e. `border-x-(--my-var)`)

## 1.10.13.1 (June 5th, 2025)

- Add Ctrl+1, Ctrl+1 shortcut for default build

## 1.10.13 (June 5th, 2025)

- Allow for build `--minify` to be specified on a per-file basis ([#115](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/115))

## 1.10.12 (May 27th, 2025)

- Support blobs for `@source`
- Fix incorrect configuration file parsing if `@source` contained a pattern like `/*.razor`
- Add support for prefixes
- Support `!important` for v4+ projects (`!` modifier is at the end, not the beginning)
- Fix a `NullReferenceException` which could occur when having custom classes added
- Fix sorting order for classes like `border-2`

## 1.10.11 (May 27th, 2025)

- Restart build if Tailwind build process runs out of memory ([#113](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/113))

## 1.10.10.1 (May 21st, 2025)

- Fix missing color previews on cultures that do not use `.` as a decimal separator ([#104](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/104))

## 1.10.10 (May 20th, 2025)

- Fix an error which could occur on Visual Studio startup, when no project is open
- Fix an error which could occur when resetting configuration files after deleting/renaming the existing one
- Fix incorrect color previews on cultures that do not use `.` as a decimal separator ([#104](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/104))

## 1.10.9 (May 18th, 2025)

- `OnSave` override build should not create an infinite amount of processes
- Fix missing descriptions for some numeric classes in projects using v4.1+
- Null blocklist should not cause errors ([#104](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/104#issuecomment-2885719490))

## 1.10.8 (May 15th, 2025)

- Candidate fix for [#104](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/104#issuecomment-2861835742)

## 1.10.7 (May 2nd, 2025)

- Class sorting should respect indents on new lines
- Add certain missing utility classes

## 1.10.6 (May 1st, 2025)

- Simplify `tailwind.extension.json` by removing `ConfigurationFiles.IsDefault` and `ConfigurationFiles.ApplicableLocations`. The extension now directly parses configuration files to find applicable locations (specified in `content` or with `@source`)
- Add blocklist support for v4.1 (`@source not inline(...)`)
- Fix handling of `color-*` and `color-___-*` in `@utility` declarations
- Add Tailwind v4.1 support
- Fix incorrect descriptions for classes like `drop-shadow-{color}` and `shadow-{color}`
- Fix inefficient caching of color icons when showing completion menu
- Fix an error which could occur when typing a class with opacity modifier without a color (i.e. `text-/`)
- Use `powershell` instead of `cmd` when building with `--watch` ([#111](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/111))

## 1.10.5 (March 23rd, 2025)

- Add a quick info message and Tailwind logos to Tailwind css directives to mitigate confusion with error tags ([#105](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/105))
- Major version update message should show major version, not minor version

## 1.10.4 (March 9th, 2025)

- Handle `@import` statements with additional parameters ([#85](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/85))
- Remove `int/float/byte.Parse()` calls to avoid format string errors ([#102](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/102))
- Singleton command filters for completion controllers ([#107](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/107))
- Reload files referenced through `@config` and `@import` ([#85](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/85))
- Correct class descriptions for ambiguous arbitrary classes (i.e. `text-[10px]`) ([#108](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/108))
- Add IntelliSense for missing css directives (`@utility`, `@custom-variant`, etc.)

## 1.10.3 (March 5th, 2025)

- Fix an error when using `var(...)` in `--color-...` theme settings ([#102](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/102))
- Handle `@import` and `@config`
- Add support for `@utility` and `@custom-variant`
- Color adornments should show up for arbitrary classes with variables (i.e. `text-(--color-blue-100)`)
- Arbitrary classes with a non-hex, three-digit value show color adornments (i.e. for `rotate-[100]`)
- Adds modifier support for `bg-conic`, `bg-linear`, `bg-radial`, `text-<size>` (i.e. `bg-conic/oklch`, `text-sm/snug`)

## 1.10.2 (March 2nd, 2025)

- Add missing classes (`bg-conic-*`, `-underline-offset-*`, etc.)
- Fix incorrect description formatting of `container`
- Configuration class support for `--color-*` and `--breakpoint-*`

## 1.10.1 (February 22nd, 2025)

- Fixed a NRE with empty CSS files ([#101](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/101))
- Fixed errors with completion filters and incorrect string formats ([#102](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/102))
- Optimize completion efficiency by lazy-loading descriptions
- Added missing descriptions and color adornments for classes like `border-x-amber-50`

## 1.10.0 (February 17th, 2025)

- Added Tailwind v4 support ([#85](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/85))
- Updated IntelliSense
    - New utilities and variants
    - Support for new () syntax (i.e. `bg-green-(--variable)`)
- Updated build (i.e. use `@tailwindcss/cli` instead)
- Updated tooltips for spacing classes and other number/percentage classes
- Parsing support for css files (partial)
- Legacy JS configuration files
- Fixed incorrect sorting for non-spacing numeric classes, color classes with modifiers (i.e. `bg-green-50/50`), and for `group-` and `peer-` variants

Upcoming --> 1.10.x:
- Plugin / `@import` support
- CSS tailwind directive and theme variable IntelliSense and linting support
- Modifier classes: (/oklch, /srgb for bg-linear, font-size/<number> for line height)

## 1.9.3 (February 8th, 2025)

- Use wanted version instead of latest version when checking for updates
- `.jsx` and `.tsx` support now works again ([#98](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/98))
- Configuration loading should be initialized after project settings have loaded ([#97](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/97))
- Fix incorrect `.jsx` and `.tsx` completion suggestions

## 1.9.2 (February 1st, 2025)

- Use existing build processes when running in JIT mode to prevent 100% RAM usage ([#84](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/84#issuecomment-2624939075))
- Search through project to find `node_modules` for `NODE_PATH` when parsing configuration files ([#87](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/87))
- Prevent empty configuration files from being generated ([#95](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/95))
- Automatic updates should consider project `tailwindcss` version, not dependency versions

## 1.9.1 (January 30th, 2025)

- Fixed a NRE when setting up Tailwind ([#93](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/93))
- Adds an option to set up Tailwind using a global installation

## 1.9.0 (January 29th, 2025)

- Added multiple project support ([#84](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/84))
- Fixed a bug which could occur when class contexts are near the end of the file
- Fixed an error which could occur when using `@tailwindcss/container-queries` ([#90](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/90))
- Properly apply custom color definitions (i.e. `backgroundColor`) when sorting classes

## 1.8.0 (January 1st, 2025)

- Added modifier descriptions for quick info tooltips
- Fixed missing support in open folder contexts ([#35](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/35))
- Show all classes in an empty context (i.e. `class="bg-green-900 |"`, where `|` represents the cursor) for a smoother IntelliSense experience
- 'Set up and install Tailwind CSS' context menu command now searches for an existing Tailwind configuration file before downloading Tailwind

## 1.7.4 (December 30th, 2024)

### Additions

- Expanded configuration file support to `tailwind.config.{js,cjs,mjs,ts,cts,mts}` ([#35](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/35))
- Better modifier descriptions for completion tooltips, excluding screen sizes

## 1.7.3 (December 12th, 2024)

### Fixes

- Fixed a NRE which could occur when no configuration file is set
- Fixed a false error when linting CSS patterns similar to `@media screen and (max-width: 768px) {`
- Classes with overriden colors (i.e. `backgroundColor`) should not have color adornments or be considered in linting

### Additions

- Improved description tooltip for completions

## 1.7.2 (December 1st, 2024)

### Fixes

- Fix an error when parsing configuration files with global packages ([#87](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/87), [#89](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/89))
- Change `tailwind.extension.json` to serialize to a human-readable format with default values for all properties
- Fixes an error which occurs when removing a configuration file

### Additions

- Add JSON `$schema` to `tailwind.extension.json` ([#88](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/pull/88))

## 1.7.1 (December 1st, 2024)

### Fixes

- Fix a crash which could occur when typing a `\` in a class context
- Fix corrupt syntax when using libraries like Alpine JS ([#86](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/86))
- Fix exceptions which could occur when using custom regexes

### Additions

- Project-based custom regexes (via `tailwind.extension.json`), instead of a global definition; check `Getting-Started.md` for more details

## 1.7.0 (November 24th, 2024)

### Fixes

- Move `tailwind.extension.json` to the root of the project containing the Tailwind configuration file ([#81](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/81))

### Additions

- Rewrite class context logic to use regexes to parse files
- Add custom class name completion contexts with custom regexes ([#79](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/79))

## 1.6.4 (October 27th, 2024)

### Additions

- Add verbose build option ([#82](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/82))

### Fixes

- Colors would not show up in IntelliSense when using a prefix ([#80](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/80))
- Use relative pathing when building Tailwind CSS ([#82](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/82))

## 1.6.3 (October 23rd, 2024)

### Fixes

- Clarify build type descriptions and add `ManualJIT` build option ([#78](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/78))

## 1.6.2 (October 21st, 2024)

### Additions

- Support for `!` modifier ([#76](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/76))

### Fixes

- Fix inaccurate color preview locations in `.css` files
- `content.transform` causes a crash ([#77](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/77))

## 1.6.1 (October 11th, 2024)

### Additions

- Add option to turn off color preview in extension settings

### Fixes

- Linter doesn't highlight conflicting classes with colors and spacing
- Legacy ASP.NET Razor editor performance improvements and bug fixes ([#72](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/72))
- Color preview should work for classes with variants (`hover:`, `focus:`, etc.) ([#74](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/74))

## 1.5.6 (1.6.0) (October 9th, 2024)

### Additions

- Add color preview for color classes ([#74](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/74))
- Support for IntelliSense in Razor variable declarations ([#73](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/73))

### Fixes

- Class sorting did not work for `.jsx` files
- Downgrade `System.Text.Json` to `6.0.10` to match .NET Framework compatibility ([#71](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/71))

## 1.5.5.2 (October 4th, 2024)

### Fixes

- Correct `.vsixmanifest` assembly versions to prevent version mismatch ([#71](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/71))
- Actually include all DLLs in output

## 1.5.5.1 (September 27th, 2024)

### Fixes

- Include all `System.Text.Json` dependencies in the extension to prevent an error ([#71](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/71))

## 1.5.5 (September 22nd, 2024)

### Additions

- `blocklist` and `corePlugins` support ([#69](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/69))
- Explicitly include `Microsoft.Bcl.AsyncInterfaces` DLL in extension to prevent an error ([#68](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/68))
 
### Fixes

- Incorrect IntelliSense order for non-color, non-spacing classes (i.e. `opacity-5` would appear between `opacity-40` and `opacity-50`)

## 1.5.4 (September 8th, 2024)

### Additions

- Quick info descriptions for non-spacing, non-color arbitrary classes ([#66](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/66))

### Fixes

- Fixed an exception which could occur when no `theme` is present in the configuration file
- Fixed an error which occurred when using `export default` instead of `module.exports`
- `extend` values should not be overridden when a plugin is defined ([#67](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/67))
- Fixed an error which occurred when reloading the configuration file before other extension components are loaded
- More accurately differentiate between hex values and rgb values in class descriptions

## 1.5.3 (September 6th, 2024)

### Fixes

- `has-[]` modifier should now be sorted to the correct spot
- Fixed unreliable descriptions for classes using spacing
- Modify incorrect sorting order ([#67](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/67))

### Additions

- Added definitions for classes defined in plugins or with custom values (i.e. `min-w-[10px]` and `bg-[#abcdef]`) ([#66](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/66))
 
## 1.5.2 (September 4th, 2024)

### Fixes

- Null reference exception when opening a `.js` file not in solution
- Fixed an error: `addDefaults` function not defined when parsing configuration files with plugins ([#65](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/65))

## 1.5.1 (September 2nd, 2024)

### Fixes

- Modified color hint in IntelliSense menu to reflect actual color in some cases ([#64](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/64))

## 1.5.0 (August 26th, 2024)

### Additions

- Ability to add multiple input/output css files ([#46](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/46))

### Fixes

- Slight performance boost on solution startup
- Fixed a bug where the update status message does not accurately reflect the newest version

### Enhancements

- Removed many status progress bars and animations to reduce clutter

## 1.4.7 (August 8th, 2024)

### Fixes

- Referencing `theme()` values which internally require `colors` (i.e. `({ colors }) => ...`) would result in incomplete IntelliSense ([#57](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/57))
- Nested theme values may not be parsed correctly ([#62](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/62))
- `!important` should be sorted to the end of the `@apply` list ([#63](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/63))
 
## 1.4.6 (August 2nd, 2024)

### Enhancements

- Setting to specify files affected by `OnSave` build option ([#61](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/pull/61))
- Class sorting should respect newlines

### Fixes

- `System cannot find the path specified` error ([#59](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/pull/59))
- `@("")` pattern in Razor could affect quick info popup description

## 1.4.5 (July 11th, 2024)

### Enhancements

- More detailed `cssConflict` error message to show which class takes precedence ([#55](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/55))
- `sm`, `md`, `lg`, `xl`, `2xl` breakpoints are now properly sorted
- Update assembly version to reflect extension version ([#56](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/56))

## 1.4.4 (July 10th, 2024)

### Additions

- Automatically add `tailwind.config.js` and `tailwind.extension.json` to project ([#52](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/52))

### Fixes

- Fix an `ArgumentOutOfRangeException` when backspacing quotation marks in `<div class="..."></div>` (related to [#54](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/54))

### Changes

- Upgrade `System.Text.Json` to `8.0.4`

## 1.4.3 (June 11th, 2024)

### Changes

- Reverted removal of `OnSave` build option ([#50](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/50))

## 1.4.2 (June 10th, 2024)

### Changes

- Removed `OnSave` build option (redundant due to Tailwind JIT)
- Added `Manual` build option, triggered by Ctrl + 1 followed by Ctrl + 2 ([#49](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/49))

## 1.4.1 (June 5th, 2024)

### Changes

- Configuration files can be `.cjs` and `.mjs` files ([#44](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/44))
- Add manual file sorting ([#45](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/45))
- Set `package.json` file path ([#47](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/47))

### Fixes

- Sort current file does not work ([#48](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/48))
- Fixed an exception which could occur when triggering IntelliSense on `@tailwind` directives
 
## 1.4.0 (May 21st, 2024)

### Additions

- Added IntelliSense, lint, quick info, and sort support for `.jsx` and `.tsx` files ([#41](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/41), [#35](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/35))

### Fixes

- Fix extra apostrophe being appended to class names ([#42](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/42))
- Fix spontaneous `COMException` which could occur when opening a project

## 1.3.2 (May 11th, 2024)

### Changes

- Razor order persisted on class sort (i.e. `class="text-gray-100 p-4 @Css"` would be sorted to `class="p-4 text-gray-100 @Css"`) ([#40](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/40))

## 1.3.1 (April 23rd, 2024)

### Fixes

- Format broken when sorting classes with Alpine JS, Vue, etc. ([#39](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/39))

## 1.3.0 (April 5th, 2024)

### Additions

- Added support for `peer` and `peer-*`
- Class sorting in `.html`, `.ascx`, `.aspx`, `.razor`, `.cshtml`, `.css`, `.tcss` files ([#29](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/29))

### Fixes

- Fixed an edge case exception which could occur when parsing `.razor` files
- Fixed an exception which could occur when backspacing in `.css` files
- Fixed an error which could occur when opening a Git compare window

## 1.2.5 (April 3rd, 2024)

### Changes

- IntelliSense no longer triggered when backspacing whitespace
- IntelliSense no longer triggered on paste
- `ts-node` dependency no longer required for `.ts` files

### Fixes

- Certain class descriptions missing (i.e. `max-w-md`)

## 1.2.4 (March 24th, 2024)

### Additions

- Support for arm64 ([#37](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/37))
- Support for standalone Tailwind CSS CLI

## 1.2.3 (March 7th, 2024)

### Fixes

- Certain `theme.colors` attributes would not appear in IntelliSense

### Additions

- Adds support for `.ts` files as configuration files

## 1.2.2 (February 7th, 2024)

### Fixes

- Fixed a `System.IO.FileNotFoundException` which could occur in rare cases ([#31](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/31))
- Linting, completions, and quick info now work in Razor files when `@` is used within class attributes

## 1.2.1 (January 20th, 2024)

### Fixes

- Fixed a bug where the `tailwind.extension.json` file preferences would be ignored ([#32](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/32))

## 1.2.0 (January 1st, 2024)

### Fixes

- Fixed an exception which could occur when typing in `css` files

### Additions

- Linting support in all related files ([#28](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/28))
- Error list support
- Linter configuration settings

## 1.1.9.3 (December 19th, 2023)

### Fixes

- Fixed an exception which could occur when Set up Tailwind CSS ([#31](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/31))
- `theme` values would sometimes be ignored in configuration files
- Fixed a bug where certain classes would still appear despite being overriden

### Enhancements

- Added an option in settings to minify builds by default
- Updated IntelliSense to support Tailwind v3.4

## 1.1.9.2 (December 5th, 2023)

### Fixes

- Fixed errors which would occur when using ES modules ([#24](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/24))

## 1.1.9.1 (December 4th, 2023)

### Fixes

- Fixed an exception which could occur when referencing plugins using `import` ([#24](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/24))

## 1.1.9 (December 3rd, 2023)

### Additions

- IntelliSense support for plugins ([#24](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/24))

### Fixes

- Default IntelliSense may still show up in some cases ([#30](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/30))

## 1.1.8.8 (November 24th, 2023)

### Additions

- Added IntelliSense support for `.tcss` files ([#27](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/27))

### Enhancements

- Using ctrl + space in the middle of a class will now display relevant completions for the entire class

### Fixes

- Fixed an exception which would occur when using `plugins` ([#26](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/26))

## 1.1.8.7 (November 5th, 2023)

### Additions

- Ability to create minified output css files
- New build option: build once when entire project is built
- `tailwind.config.js` files will automatically be found when opening an existing project ([#22](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/22))

### Enhancements

- Renamed certain menu items to better reflect their purpose ([#23](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/23))
- Performance improvements when typing `/` for transparency variants

## 1.1.8.6 (October 29th, 2023)

### Additions

- Support for `({ theme })` in configuration files

### Fixes

- Minor performance fixes for transparency IntelliSense

## 1.1.8.5 (September 20th, 2023)

### Additions

- Support for `CssClass` in ASP.NET Web Forms controls ([#20](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/20))
- Full description support for `fontSize`

### Fixes

- Fixed a bug where custom descriptions would sometimes fail to appear

## 1.1.8.4 (September 8th, 2023)

### Additions

- Support for `prefix` values ([#19](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/19))
- Added description support for `fontFamily` and `dropShadow` (`fontSize` not supported)

## 1.1.8.3 (August 30th, 2023)

### Fixes

- Added extra guard clauses for QuickInfo tooltips ([#17](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/17))
- Moved entire package initialization to be executed on a background thread (performance issues cited in [#17](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/17))

## 1.1.8.2 (August 29th, 2023)

### Fixes

- Fixed QuickInfo tooltips showing more than 1 description ([#17](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/17))
- QuickInfo tooltips would not show up with `whitespace-...` variants
- Fixed an issue where backspacing from the end of a class would not show other completions

### Enhancements

- Added `px` values for non-spacing `rem` values

## 1.1.8.1 (August 25th, 2023)

### Enhancements

- IntelliSense now scrolls correctly when filtering (i.e. typing `border-b` will now prioritize `border-b` instead of `border-blue-*`)

### Fixes

- IntelliSense did not show any modifiers (i.e. `active:`, `focus:`) in certain cases
- Fixed an exception which would occur when hovering over Tailwind classes before the project was loaded
- Build process would build to default file when adding a new project (related to [#14](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/14))

## 1.1.8 (August 23rd, 2023)

### Additions

- Re-written configuration file parser to support referencing other files (i.e. using `require('tailwindcss/defaultTheme')` to reference the default theme)
- Added hover tooltips for Tailwind classes

### Fixes

- Error loading `tailwind.config.js` when certain properties are used ([#16](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/16))

### Enhancements

- Changed Tailwind update check to be asynchronous to prevent UI locking

## 1.1.7.8 (August 21st, 2023)

### Fixes

- Classes would not show up consistently with IntelliSense ([#14](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/14))
- Spacing mapper now works when local culture uses commas instead of dots ([#15](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/15))
- Fixed an issue where `tailwind.extension.json` could not be found when multiple projects are loaded

## 1.1.7.7 (August 19th, 2023)

### Fixes

- Added null checks where errors could occur ([#14](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/14))

## 1.1.7.6 (August 18th, 2023)

### Fixes

- Exceptions fixed when IntelliSense triggered with certain configuration files ([#8](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/8))
- Completions no longer show up in a non-Tailwind project

### Enhancements

- IntelliSense now shows up with Blazor components using a `Class=""` parameter instead of `class=""` ([#12](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/12))
- Improved IntelliSense filtering: for example, typing `hue-60` will now display `backdrop-hue-rotate-60`

## 1.1.7.5 (August 17th, 2023)

### Fixes

- Null reference exception would occur when IntelliSense triggered ([#8](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/8))

## 1.1.7.4 (August 16th, 2023)

### Enhancements

- Build settings are now updated when `tailwind.extension.json` is directly modified ([#11](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/11))

## 1.1.7.3 (August 14th, 2023)

### Fixes

- Fixed incorrect descriptions for negative spacing values
- CSS output file becomes null when file does not exist ([#11](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/11))
- Build menu in an open folder context would not show up (moved toggle to solution explorer folder node)

## 1.1.7.2 (August 10th, 2023)

### Enhancements

- Added `px` values for spacing in completion descriptions ([#9](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/9))

## 1.1.7.1 (August 9th, 2023)

### Fixes

- Fixed an exception which would occur with specific configuration files ([#8](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/8))
- Fixed a bug where custom colors would not show up in IntelliSense

## 1.1.7 (August 8th, 2023)

### Additions

- CSS descriptions for each class ([#7](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/7))
- Added support for missing configuration values

### Fixes

- rgb() values now supported within `theme.colors` and `theme.extend.colors`
- Spacing values (i.e. padding, margin) now ordered correctly

## 1.1.6 (August 3rd, 2023)

### Additions

- Support for Web Forms files (.aspx, .ascx, .master) ([#6](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/6))

### Fixes

- `package.json` script would sometimes not update correctly

## 1.1.5 (August 2nd, 2023)

### Enhancements

- IntelliSense shows up if you backspace or type at the end of a class
- Added colons to the end of each modifier for clarity
- Removed modifiers showing up before each completion

### Additions

- Support for `max-{breakpoint}:`
- Support for group and all group modifiers

## 1.1.4.2 (July 31st, 2023)

### Fixes

- Fixed a bug where configuration values would not update
- Fixed an error which would occur when backspacing in empty CSS files

## 1.1.4.1 (July 31st, 2023)

### Fixes

- Fixed a bug where completions would not show up

## 1.1.4 (July 31st, 2023)

### Additions

- Support for most configuration specifications
- Added ability to override the default build process or supplement it with a package.json script ([#5](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/5))

### Enhancements

- Added arbitrary completions for all those that support it

### Fixes

- Tailwind completions would persist when switching to another project, even if it does not have Tailwind enabled

## 1.1.3 (July 28th, 2023)

### Fixes

- Minor bug fix: wrong method being unsubscribed in `TailwindBuildProcess` ([#3](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/pull/3))
- Fixed an issue where the status bar loading animation would continue to play

## 1.1.2 (July 26th, 2023)

### Fixes

- Added missing classes

## 1.1.1 (July 26th, 2023)

### Enhancements

- Better IntelliSense color sorting ([#1](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/1))
- Contains IntelliSense filtering instead of starts with ([#1](https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/1))
- Status bar and output pane now shows more information on build (time taken, time when built)

### Fixes

- Fixed a bug where CSS completion would not commit

## 1.1.0 (July 25th, 2023)

### Additions

- IntelliSense transparency support for colors (i.e. `bg-green-700/50`)
- New build option - On Save: runs an `npx` build command on each file save; can be used if default build option is not reliable

### Fixes

- Fixed more classes which were not previously showing up in IntelliSense

## 1.0.3 (July 24th, 2023)

### Fixes

- Fixed certain incorrect/missing classes in IntelliSense

## 1.0.2 (July 21st, 2023)

### Fixes

- Build would start in projects with no Tailwind configuration
- Fixed an issue where the `npx tailwind --watch` process would fail to terminate

## 1.0.1 (July 20th, 2023)

### Fixes

- Fixed an error which would sometimes occur when switching between projects
