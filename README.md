# LightControl for LINQPad

Control your Hue compatible lights with C#!

## Getting started

The first time you run the program, an application key will attempt to be generated. While the program is waiting, press the sync button on your base station. The application key will be stored in the LINQPad Password Manager, and be used during subsequent runs.

Change `"Office"` to whatever room you want to control, and start using methods of `LightControl`.

## Example

```csharp
async Task Main()
{
	var L = new LightControl("Office");

	Hue hue = Hue.Blue;
	await L.SetHueAsync(hue, 254, 3000);
	await L.DimAsync(80);
	await (await (await L.SwitchAsync(true)).SwitchAsync(false)).SwitchAsync(true);

}
```
