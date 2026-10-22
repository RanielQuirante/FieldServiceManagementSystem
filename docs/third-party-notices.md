# Third-party notices

FieldService includes or draws on the third-party material listed here. Each item keeps its own licence terms, whatever licence applies to the rest of the repository.

## Contents

- [Fonts: Geist and Geist Mono](#fonts-geist-and-geist-mono)
- [Icons: Lucide and Feather](#icons-lucide-and-feather)
- [Windows platform packages of the technician app](#windows-platform-packages-of-the-technician-app)
- [Package licences](#package-licences)

## Fonts: Geist and Geist Mono

The dashboard ships seven Geist and Geist Mono font files from release `v1.8.1` of the Geist font project (https://github.com/vercel/geist-font/releases/tag/v1.8.1).

```text
Copyright 2024 The Geist Project Authors (https://github.com/vercel/geist-font)

This Font Software is licensed under the SIL Open Font License, Version 1.1.
```

The full licence text travels with the font files in `src/FieldService.Web/src/app/fonts/OFL.txt`. The technician app ships six TrueType files from the same release (Geist 400, 500, 600 and 700, Geist Mono 500 and 600) in `src/FieldService.Mobile/Resources/Fonts/`, with an identical copy of the licence text, `OFL.txt`, beside them. The fonts are used unmodified, and the licence names no Reserved Font Name.

## Icons: Lucide and Feather

The icons in `src/FieldService.Web/src/shared/ui/icons.ts` were drawn for FieldService. Eleven of them share path data with Lucide 1.59.0 or Feather 4.29.2: check, chevron-down, chevron-left, chevron-right, clipboard-check, log-out, message, minus, user-check, wifi-off and wrench. Other icons draw shapes common to many icon sets. Both notices therefore apply to the icon set, and the technician app draws the same path data under the same notices.

### Lucide (ISC License)

```text
ISC License

Copyright (c) 2026 Lucide Icons and Contributors
Portions copyright (c) 2013-present Cole Bemis, as part of Feather (MIT)

Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted, provided that the above
copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
```

### Feather (MIT License)

```text
The MIT License (MIT)

Copyright (c) 2013-2023 Cole Bemis

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Windows platform packages of the technician app

The Windows build of the technician app restores these packages through .NET MAUI. They are not part of the Android build or of any other application in the repository.

| Package | Version | Terms |
|---|---|---|
| Microsoft.WindowsAppSDK | 1.8.260529003 | The Microsoft licence terms published with the package (https://www.nuget.org/packages/Microsoft.WindowsAppSDK/1.8.260529003) |
| Microsoft.Graphics.Win2D | 1.4.0 | The Microsoft licence terms published with the package (https://www.nuget.org/packages/Microsoft.Graphics.Win2D/1.4.0) |
| Microsoft.Web.WebView2 | 1.0.3179.45 | The Microsoft licence terms published with the package (https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.3179.45) |
| Microsoft.Windows.SDK.BuildTools | 10.0.26100.8249 | The Microsoft licence terms published with the package (https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools/10.0.26100.8249) |
| Microsoft.Maui.Graphics.Win2D.WinUI.Desktop | 10.0.110 | MIT |

These terms are not open-source licences. They cover use of the packages in the Windows build and are checked again when the Windows build is verified on a Windows host.

## Package licences

Every other dependency is a package named in a project manifest (`*.csproj` with `Directory.Packages.props`, or `package.json` with its lock file), and its licence is the one its package declares. The licences are checked during dependency reviews, and the npm packages of this repository are private and never published.
