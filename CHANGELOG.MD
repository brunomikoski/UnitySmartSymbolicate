# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

## [1.1.1]
### Changed
 - Fixed formatting to expose all previous information as well, since can hide valueable information
 - Improved Hyperlink generator to find target classes

## [1.1.0]
### Changed
 - Better output format

### Add
- Added new parser that will try to find that file and line number of `libil2cpp` errors

## [1.0.3]
### Changed
- Allowed custom symbols to be used besides the know ones

### Fix
- Fixed an issue that when parsing crashes would still use the unity symbols from unity installation instead of the project ones when Strip Engine Code is enabled


## [1.0.2]
### Changed
- Moved the Menu to `Window/Analysis/Smart Symbolicate`

## [1.0.1]
### Add
- Added a symbol selection option

## [1.0.0]
### Add
- Initial implementation of the Smart Symbolicate

[1.1.1]: https://github.com/brunomikoski/UnitySmartSymbolicate/releases/tag/v1.1.1
[1.1.0]: https://github.com/brunomikoski/UnitySmartSymbolicate/releases/tag/v1.1.0
[1.0.3]: https://github.com/brunomikoski/UnitySmartSymbolicate/releases/tag/v1.0.3
[1.0.2]: https://github.com/brunomikoski/UnitySmartSymbolicate/releases/tag/v1.0.2
[1.0.1]: https://github.com/brunomikoski/UnitySmartSymbolicate/releases/tag/v1.0.1
[1.0.0]: https://github.com/brunomikoski/UnitySmartSymbolicate/releases/tag/v1.0.0
