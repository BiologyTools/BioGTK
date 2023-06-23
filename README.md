# BioGTK
![alt text](https://github.com/BiologyTools/Bio/blob/master/banner.jpg)

A .NET application & library for editing & annotating various microscopy image formats. Supports all bioformats supported images. Integrates with ImageJ, running ImageJ filters & macro functions. Check out the new wiki for [library usage.](https://github.com/BiologyTools/Bio/wiki/Library-Usage) or check out the [documentation.](https://biologytools.github.io/) Supports Windows, Linux and Mac. For Windows & Mac see installation instructions. For Discussion check out the new Discord Server. https://discord.gg/tdeyc6fgpv

[![NuGet version (BioGTK)](https://img.shields.io/nuget/v/BioGTK.svg?style=flat-square)](https://www.nuget.org/packages/BioGTK/3.1.0)
[![NuGet version (BioGTK)](https://img.shields.io/nuget/dt/BioGTK?color=g)](https://www.nuget.org/packages/BioGTK/3.1.0)
## Features

- C# scripting with sample tool-script and other sample scripts in "/Scripts/" folder. [See samples](https://github.com/BioMicroscopy/BioImage-Scripts)

- Supports running ImageJ macro commands on images open in Bio. Console to run ImageJ macro commands and Bio C# scripts.

- Supports Pyramidal images with multiple resolutions. Like whole slide images.

- Multiple view modes like Emission, and Filtered. ROI's shown for each channel can be configured in ROI Manager.

- Supports drawing shapes & colors onto 16 bit & 48 bit images, unlike System.Drawing.Graphics.

- Convenient viewing of image stacks with scroll wheel moving Z-plane and mouse side buttons scrolling C-planes.

- Editing & saving ROI's in images to OME format image stacks.

- Copy & Paste to quickly annotate images and name them easily by right click.

- Select multiple points by holding down control key, and move them by holding down control key. 

- Exporting ROI's from each OME image in a folder of images to CSV.

- Easy freeform annotation with magic select tool which selects based on blob detection.

## Dependencies
- [BioFormats.Net](https://github.com/GDanovski/BioFormats.Net)
- [IKVM](http://www.ikvm.net/)
- [AForge](http://www.aforgenet.com/)
- [LibTiff.Net](https://bitmiracle.com/libtiff/)
- [Cs-script](https://github.com/oleg-shilo/cs-script/blob/master/LICENSE)
- [ImageJ](https://imagej.nih.gov/ij/) (Only needed when running ImageJ macro commands)
- [Oxyplot](https://oxyplot.github.io/)

## Licenses
- BioGTK [GPL3](https://www.gnu.org/licenses/gpl-3.0.en.html)
- AForge [LGPL](http://www.aforgenet.com/framework/license.html)
- BioFormats.Net [GPL3](https://www.gnu.org/licenses/gpl-3.0.en.html)
- [IKVM](https://github.com/gluck/ikvm/blob/master/LICENSE)
- LibTiff.Net [BSD](https://bitmiracle.com/libtiff/)
- Cs-script [MIT](https://github.com/oleg-shilo/cs-script/blob/master/LICENSE)
- Oxyplot [MIT](https://github.com/oxyplot/oxyplot/blob/develop/LICENSE)

## Scripting
-  Save scripts into "StartupPath/Scripts" with ".cs" ending.
-  Open script editor and recorder from menu.
-  Scripts saved in Scripts folder will be loaded into script runner.
-  Use Script recorder to record program function calls and script runner to turn recorder text into working scripts. (See sample [scripts](https://github.com/BioMicroscopy/BioImage-Scripts)

## Mac Installation
- Install Mac package manager [homebrew.](https://brew.sh/)
- From brew install [GTK3.](https://formulae.brew.sh/formula/gtk+3#default)
- Download the BioGTK application for either OSX-x64 or OSX-Arm from releases.
- Make the file executable by opening terminal in the extracted folder and running "chmod 755 BioGTKApp" 

## Windows Installation
- Install package manager [MSYS2.](https://github.com/GtkSharp/GtkSharp/wiki/Installing-Gtk-on-Windows) to install package GTK3. (Required for GTK Apps.)
- Download the BioGTK Windows installer from releases.

## Linux Installation
- Just download the latest tarball(tar.gz) from Releases as linux already includes GTK3 package.

## Sample Tool Script
```
//css_reference BioGTK.dll; 
using System; 
using BioGTK;
using System.Threading;
using AForge;
public class Loader {

//Point ROI Tool Example
public string Load()
{
	int ind = 1;
	do
	{
		BioGTK.Scripting.State s = BioGTK.Scripting.GetState();
		if (s != null)
		{
			if (!s.processed)
			{
				if (s.type == BioGTK.Scripting.Event.Down && s.buts == 1)
				{
					ZCT cord = BioGTK.App.viewer.GetCoordinate();
					BioGTK.Scripting.LogLine(cord.ToString() + " Coordinate");
					BioGTK.ROI an = BioGTK.ROI.CreatePoint(cord, s.p.X, s.p.Y);
					BioGTK.ImageView.SelectedImage.Annotations.Add(an);
					BioGTK.Scripting.LogLine(cord.ToString() + " Coordinate");
					an.Text = "Point" + ind;
					ind++;
					BioGTK.Scripting.LogLine(s.ToString() + " Point");
					//ImageView.viewer.UpdateOverlay();
				}
				else
				if (s.type == BioGTK.Scripting.Event.Up)
				{
					BioGTK.Scripting.LogLine(s.ToString());
				}
				else
				if (s.type == BioGTK.Scripting.Event.Move)
				{
					BioGTK.Scripting.LogLine(s.ToString());
				}
				s.processed = true;
			}
		}
		if(BioGTK.Scripting.Exit("points.cs"))
		{	
			return "OK";
		}
	} while (true);
	return "OK";
}
}
```
