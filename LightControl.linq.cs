// Copyright (c) 2019 Maxwell Bloch, all rights reserved
// License: AGPLv3

async Task Main()
{
	var L = new LightControl("Office");

	//	Hue hue = Hue.Blue;
	//	await L.SetHueAsync(hue, 254, 3000);
	//	await L.DimAsync(80);
	//  await (await (await L.SwitchAsync(true)).SwitchAsync(false)).SwitchAsync(true);
	
	await L.SwitchAsync(false);
}

public enum Hue
{
	Red = 0,
	Orange = 8192 * 1,
	Yellow = 8192 * 2,
	Green = 8192 * 3,
	White = 8192 * 4,
	Blue = 8192 * 5 + 4096,
	Purple = 8192 * 6,
	Magenta = 8192 * 7,
	Pink = 8192 * 7 + 4096,
	Rose = 8192 * 7 + 4096 + 2048,
}

class LightControl
{
	protected static readonly string Copyright = "Copyright (c) 2019 Maxwell Bloch, all rights reserved";
	protected static readonly string License = $@"
	LightControl - C#/.NET and LINQPad code to interface with Hue-compatible Light API
	{Copyright}
	
	This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as
    published by the Free Software Foundation, either version 3 of the
    License.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.

    You should have received a copy of the GNU Affero General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.";
	
	private string Room { get; }

	private static readonly string ApplicationName = "LINQPad";
	private static string PartialGuid() => Guid.NewGuid().ToString().Substring(Guid.Empty.ToString().Length - 8);
	private static readonly string DeviceName = $"application{PartialGuid()}";

	public static bool Debug = true;
	internal static Action<object> DebugLog = _ =>
	{
		if (Debug) { _.Dump(); }
	};
	internal static Action<object, string> DebugLabelled = (_, __) =>
	{
		if (Debug) { _.Dump(__); }
	};

	public static async Task<string> RegisterAsync(LocalHueClient client)
	{
		Enumerable.Range(0, 10)
			.ToList()
			.ForEach(t =>
				{
					DebugLog(10 - t);
					Thread.Sleep(1000);
				}
			);
		var appKey = await client.RegisterAsync(ApplicationName, DeviceName);
		DebugLabelled(appKey, "Use this appKey value");
		return appKey;
	}

	private static async Task<LocalHueClient> AutoClientAsync()
	{
		var locator = new HttpBridgeLocator();
		var bridges = await locator.LocateBridgesAsync(TimeSpan.FromSeconds(5));
		var ip = bridges.FirstOrDefault()?.IpAddress;
		if (ip == null)
		{
			throw new ApplicationException("No Hue bridges found");
		}
		var client = new LocalHueClient(ip);
		return client;
	}

	public static async Task<string> RegisterAsync() => await RegisterAsync(await AutoClientAsync());

	private static async Task<LocalHueClient> AssertInitializedAsync(LocalHueClient client, string appKey)
	{
		if (await new Func<Task<bool>>(async () =>
		{
			client.Initialize(appKey);
			return !(await client.CheckConnection());
		}).Invoke())
		{
			appKey = await RegisterAsync(client);
			throw new ApplicationException($"Invalid AppKey - try using \"{appKey}\"");
		}
		return client;
	}

	private static async Task<LocalHueClient> GetClientAsync() => await AssertInitializedAsync(await AutoClientAsync(), Util.GetPassword("Hue Bridge AppKey"));

	public LightControl(string room)
	{
		var init = Client.GetGroupsAsync()
			.ContinueWith(g => g.Result.Where(l => l.Name == room).Single())
			.ContinueWith(g =>
				{
					var group = g.Result;
					var state = group.Action;
					var groupState = group.State;
					var lightStates = group.Lights
						.Select(async l => new { Light = l, State = (await Client.GetLightAsync(l)).State })
						.Select(ls => ls.Result)
						.ToDictionary(
							ls => ls.Light,
							ls => ls.State
						);
					return new
					{
						State = state,
						GroupState = groupState,
						LightStates = lightStates
					};
				}
			)
			.Result;
		this.InitState = init.State;
		this.GroupState = init.GroupState;
		this.InitLightStates = init.LightStates;
		this.Lights = InitLightStates.Select(l => l.Key).ToList();
	}

	private LocalHueClient Client { get; } = GetClientAsync().ContinueWith(c => c.Result).Result;

	private State InitState { get; }

	private Q42.HueApi.Models.Groups.GroupState GroupState { get; }

	private IDictionary<string, State> InitLightStates { get; }

	public List<string> Lights { get; }

	private async Task<State> GetStateAsync(string light) => (await Client.GetLightAsync(light)).State;

	public async Task<int> GetHueAsync(string light) => (await GetStateAsync(light)).Hue.Value;

	public async Task<IEnumerable<int>> GetHuesAsync() => await Task.WhenAll(Lights.Select(async light => await GetHueAsync(light)));

	private async Task<LightControl> SendCommandAsync(LightCommand lc, int delayMs = 250, Action<Light> then = null)
	{
		await Task<LightControl>.WhenAll(Lights.Select(async l =>
		{
			await Client.SendCommandAsync(lc, new[] { l });
			await Task<LightControl>.Delay(delayMs);
			then?.Invoke(await Client.GetLightAsync(l));
		}));
		return this;
	}

	public async Task<LightControl> SetColorAsync(double x, double y, int delayMs = 250) => await SendCommandAsync(new LightCommand
	{
		ColorCoordinates = new[] { ((x, x) = (Math.Max(0, x), Math.Min(1, x))).Item1, ((y, y) = (Math.Max(0, y), Math.Min(1, y))).Item1 }
	}, delayMs);

	public async Task<LightControl> SetHueAsync(Hue hue, int saturation = 254, int delayMs = 250) => await SetHueAsync((int)hue, saturation, delayMs);

	public async Task<LightControl> SetHueAsync(int hue, int saturation = 254, int delayMs = 250) => await SendCommandAsync(new LightCommand { Hue = hue % 65536, Saturation = saturation }, delayMs);

	public async Task<LightControl> DimAsync(byte b, int delayMs = 250) => await SendCommandAsync(new LightCommand { Brightness = ((b, b) = (Math.Max(byte.MinValue, b), Math.Min(byte.MaxValue, b))).Item1 }, delayMs);

	public async Task<LightControl> TempAsync(int t, int delayMs = 250) => await SendCommandAsync(new LightCommand { ColorTemperature = t }, delayMs);

	public async Task<LightControl> SwitchAsync(bool on, int delayMs = 250) => await SendCommandAsync(new LightCommand { On = on }, delayMs);

	public async Task RestoreAsync()
	{
		await Task.WhenAll(InitLightStates.Select(async ls =>
		{
			var c = new LightCommand
			{
				Brightness = ls.Value.Brightness,
				ColorCoordinates = ls.Value.ColorCoordinates,
				ColorTemperature = ls.Value.ColorTemperature,
				Hue = ls.Value.Hue,
				Saturation = ls.Value.Saturation,
				On = ls.Value.On
			};
			await Client.SendCommandAsync(c, new[] { ls.Key });
		}));
	}
}
