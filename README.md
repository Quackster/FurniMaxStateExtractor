# FurniMaxStateExtractor

A .NET library for analyzing Habbo furni SWF files and extracting the maximum number of animation states (MaxStates) defined in their embedded visualization XML.

## Features

- Supports compressed (CWS) and uncompressed (FWS) SWF files
- Extracts embedded binary XML visualization data
- Parses animation states and returns MaxStates

## Usage

```csharp
using HabboFurniTools;

// Extract MaxStates from the SWF
int maxStates = FurniMaxStateExtractor.GetMaxStatesFromSwf(@"path/to/your/furni.swf"=);

if (maxStates == -1)
{
    Console.WriteLine("No visualization data found or SWF invalid.");
}
else
{
    Console.WriteLine($"Max states in this furni: {maxStates}");
}
```
