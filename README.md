# Unity Smart Symbolicate

<img src=https://user-images.githubusercontent.com/600419/164972718-570c05f4-5233-440b-94dd-f3a39223742e.png />

<p >
    <a href="https://github.com/brunomikoski/UnitySmartSymbolicate/blob/master/LICENSE.md">
		<img alt="GitHub license" src ="https://img.shields.io/github/license/Thundernerd/Unity3D-PackageManagerModules" />
	</a>

</p> 

<p >
    <a href="https://openupm.com/packages/com.brunomikoski.unitysmartsymbolicate/">
        <img src="https://img.shields.io/npm/v/com.brunomikoski.unitysmartsymbolicate?label=openupm&amp;registry_uri=https://package.openupm.com" />
    </a>

  <a href="https://github.com/brunomikoski/UnitySmartSymbolicate/issues">
     <img alt="GitHub issues" src ="https://img.shields.io/github/issues/brunomikoski/UnitySmartSymbolicate" />
  </a>

  <a href="https://github.com/brunomikoski/UnitySmartSymbolicate/pulls">
   <img alt="GitHub pull requests" src ="https://img.shields.io/github/issues-pr/brunomikoski/UnitySmartSymbolicate" />
  </a>
  
  <img alt="GitHub last commit" src ="https://img.shields.io/github/last-commit/brunomikoski/UnitySmartSymbolicate" />
</p>

<p align="center">
    	<a href="https://github.com/brunomikoski">
        	<img alt="GitHub followers" src="https://img.shields.io/github/followers/brunomikoski?style=social">
	</a>	
	<a href="https://twitter.com/brunomikoski">
		<img alt="Twitter Follow" src="https://img.shields.io/twitter/follow/brunomikoski?style=social">
	</a>
</p>


<p align="center">

</p>


## Features
- Automatically identify Google Play Crash reports information and settings

## How to use?
- Define unity installation folder (It should be the folder where all your unity installations are)
- Define your project [symbols](https://docs.unity3d.com/2020.3/Documentation/Manual/android-symbols.html)  folder
- Paste google play crash rates on the input
- Validate information
- Press Parse

## FAQ

## System Requirements
Unity 2018.4.0 or later versions


## How to install

	
	
<details>
<summary>Add from OpenUPM <em>| via scoped registry, recommended</em></summary>

This package is available on OpenUPM: https://openupm.com/packages/com.brunomikoski.animationsequencer

To add it the package to your project:

- open `Edit/Project Settings/Package Manager`
- add a new Scoped Registry:
  ```
  Name: OpenUPM
  URL:  https://package.openupm.com/
  Scope(s): com.brunomikoski
  ```
- click <kbd>Save</kbd>
- open Package Manager
- click <kbd>+</kbd>
- select <kbd>Add from Git URL</kbd>
- paste `com.brunomikoski.unitysmartsymbolicate`
- click <kbd>Add</kbd>
</details>

<details>
<summary>Add from GitHub | <em>not recommended, no updates :( </em></summary>

You can also add it directly from GitHub on Unity 2019.4+. Note that you won't be able to receive updates through Package Manager this way, you'll have to update manually.

- open Package Manager
- click <kbd>+</kbd>
- select <kbd>Add from Git URL</kbd>
- paste `https://github.com/brunomikoski/UnitySmartSymbolicate.git`
- click <kbd>Add</kbd>
</details>


