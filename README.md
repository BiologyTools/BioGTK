# BioGTK
 ![alt text](https://github.com/BiologyTools/Bio/blob/master/banner.jpg)

# Bio

A .NET application & library for editing & annotating various microscopy image formats. Supports all bioformats supported images. Integrates with ImageJ, running ImageJ filters & macro functions. Download the prebuilt [sample application](https://github.com/BiologyTools/BioSampleApp) Also check out the new wiki for [library usage.](https://github.com/BiologyTools/Bio/wiki/Library-Usage) or check out the [documentation.](https://biologytools.github.io/) Supports Windows, Linux and Mac.

## Features

- C# scripting with sample tool-script and other sample scripts in "/Scripts/" folder. [See samples](https://github.com/BioMicroscopy/BioImage-Scripts)

- Supports running ImageJ macro commands on images open in Bio. Console to run ImageJ macro commands and Bio C# functions.

- Supports drawing shapes & colors onto 16 bit & 48 bit images, unlike System.Drawing.Graphics.

- Convenient viewing of image stacks with scroll wheel moving Z-plane and mouse side buttons scrolling C-planes.

- Editing & saving ROI's in images to OME format image stacks.

- Use AForge filters by opening filters tool window and right click to apply. Currently supports only some AForge filters as many of them do not support 16bit and 48bit images. Convert to 8bit image to make use of more filters. Applyed filters can be easily recorded and used in scripts. Bio impliments some filters like crop for 16 & 48 bit images.

- `Star this project on Github to help spread the word about Bio!`

## Dependencies
- [BioFormats.Net](https://github.com/GDanovski/BioFormats.Net)
- [IKVM](http://www.ikvm.net/)
- [AForge](http://www.aforgenet.com/)
- [LibTiff.Net](https://bitmiracle.com/libtiff/)
- [Cs-script](https://github.com/oleg-shilo/cs-script/blob/master/LICENSE)
- [ImageJ](https://imagej.nih.gov/ij/) (Only needed when running ImageJ macro commands)

## Licenses
- BioGTK [GPL3](https://www.gnu.org/licenses/gpl-3.0.en.html)
- AForge [LGPL](http://www.aforgenet.com/framework/license.html)
- BioFormats.Net [GPL3](https://www.gnu.org/licenses/gpl-3.0.en.html)
- [IKVM](https://github.com/gluck/ikvm/blob/master/LICENSE)
- LibTiff.Net [BSD](https://bitmiracle.com/libtiff/)
- Cs-script [MIT](https://github.com/oleg-shilo/cs-script/blob/master/LICENSE)

## Scripting
-  Save scripts into "StartupPath/Scripts" with ".cs" ending.
-  Open script editor and recorder from menu.
-  Double click on script name in Script runner to run script.
-  Scripts saved in Scripts folder will be loaded into script runner.
-  Program installer include sample filter & tool script.
-  Use Script recorder to record program function calls and script runner to turn recorder text into working scripts. (See sample [scripts](https://github.com/BioMicroscopy/BioImage-Scripts)
