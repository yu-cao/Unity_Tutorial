## Unity学习记录——每秒帧数

本文主要讲对于Unity性能等的测定与考量

为了定期生成，我们需要跟踪自上次生成以来的时间。我们可以使用简单的FixedUpdate方法完成此操作。（**FixedUpdate和Update区别**：FixedUpdate可以使得画面产生于帧速率无关，如果两个spawn中配置的时间小于帧时间，使用Update会导致延迟。我们可以使用`while`代替`if`来check错过的spawn，但是当`timeSinceLastSpawn`被意外设置为0时会导致无限循环，所以这里每个固定时间间隔进行一次spawning是一个更好的策略）

```c#
	float timeSinceLastSpawn;

	void FixedUpdate () {
		timeSinceLastSpawn += Time.deltaTime;
		if (timeSinceLastSpawn >= timeBetweenSpawns) {
			timeSinceLastSpawn -= timeBetweenSpawns;
			SpawnNucleon();
		}
	}
	void SpawnNucleon () {
		Nucleon prefab = nucleonPrefabs[Random.Range(0, nucleonPrefabs.Length)];
		Nucleon spawn = Instantiate<Nucleon>(prefab);
		spawn.transform.localPosition = Random.onUnitSphere * spawnDistance;
	}
```

现在按照Unity的设置启动，我们可以在stats界面中看到一系列数据，但是如果我们需要更为详细的数据，需要通过Windows/Analysis/Profiler中的分析器来判定，尤其是CPU与内存的数据

有时候，我们需要关闭垂直同步`vsync`，因为它会影响CPU的性能使得其判定在开始时不够精准，我们往往想知道的是我们的场景到底需要多少CPU的资源。可以通过*Edit / Project Settings / Quality*来关闭它（即V Sync Count设置改成 Don't Sync）

但是关闭垂直同步会带来一个问题：在简单场景中帧数实在太高了，（FPS常常高达500+，给硬件带来压力）可以通过设置Application.targetFrameRate属性来控制

我们也许会观察到莫名其妙有几个突然较大的GC，这实际上是我们在操作Editor导致的，如果我们需要从测量中去除Editor本身，则必须独立构建。当我们进行development build的时候我们依然可以使用profiler，甚至在这种情况下当运行App时会自动连接，我们可以在 *File / Build Settings*中进行选择

这时，内存分配将只会由产生的核子构成，而不再发生GC。这个场景中，主要是渲染，脚本的影响几乎可以忽略不计

测量每秒的帧数

之前的profile给我们很多信息，但是依然没有给我们一个好的帧率的测定，FPS=1/CPU时间，这不是我们真正的帧率，我们手动进行测试：

```c#
using UnityEngine;

public class FPSCounter : MonoBehaviour
{
    public int FPS
    {
        get;
        private set;
    }
    
    void Update()
    {
        //注意这里的时间是未经过缩放的时间（真实时间），而不是简单的deltaTime
        FPS = (int) (1f / Time.unscaledDeltaTime);
    }
}
```

我们如何显示这个FPS呢？我们使用Unity's UI来进行显示。创建一个Canvas，其中有一个包含text object的panel，我们可以通过*GameObject / UI*的子目录进行添加

我们创建以下脚本进行添加：

```c#
//FPSCounter.cs
using UnityEngine;

public class FPSCounter : MonoBehaviour {

	public int FPS { get; private set; }
	void Update () {
    //注意这里的时间是未经过缩放的时间（真实时间），而不是简单的deltaTime
		FPS = (int)(1f / Time.unscaledDeltaTime);
	}
}

//FPSDisplay.cs
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(FPSCounter))]
public class FPSDisplay : MonoBehaviour {

	public Text fpsLabel;
	FPSCounter fpsCounter;

	void Awake () {
		fpsCounter = GetComponent<FPSCounter>();
	}

	void Update () {
		//fpsLabel.text = fpsCounter.FPS.ToString();//如果超过99，显示会出错
		fpsLabel.text = Mathf.Clamp(fpsCounter.FPS, 0, 99).ToString();//限制在0~99中，大于的统一显示为99
	}
}
```

我们现在正在为每次更新创建一个新的字符串对象，在下次更新时将其丢弃。这会污染托管内存，这将触发垃圾收集器。虽然这对于桌面应用来说并不是什么大不了的事，但对于内存很少的设备而言，它更加麻烦。它还会污染我们的分析器数据，这使寻找allocation变得困难。

改进策略：直接打表：

```c#
	static string[] stringsFrom00To99 = {
		"00", "01", "02", "03", "04", "05", "06", "07", "08", "09",
		"10", "11", "12", "13", "14", "15", "16", "17", "18", "19",
		"20", "21", "22", "23", "24", "25", "26", "27", "28", "29",
		"30", "31", "32", "33", "34", "35", "36", "37", "38", "39",
		"40", "41", "42", "43", "44", "45", "46", "47", "48", "49",
		"50", "51", "52", "53", "54", "55", "56", "57", "58", "59",
		"60", "61", "62", "63", "64", "65", "66", "67", "68", "69",
		"70", "71", "72", "73", "74", "75", "76", "77", "78", "79",
		"80", "81", "82", "83", "84", "85", "86", "87", "88", "89",
		"90", "91", "92", "93", "94", "95", "96", "97", "98", "99"
	};
	
	void Update () {
		fpsLabel.text = stringsFrom00To99[Mathf.Clamp(fpsCounter.FPS, 0, 99)];
	}
```

我们接下来要想办法平均化每秒的帧数，因为只显示瞬时帧数在帧数不稳定时标签会疯狂波动，这将会难以得到有效读数

我们修改之前的FPSCounter.cs，使之变成显示平均帧数的记录器

```c#
using System.Xml.Serialization;
using UnityEngine;

public class FPSCounter : MonoBehaviour
{
    public int AverageFPS
    {
        get;
        private set;
    }

    public int frameRange = 60;

    private int[] fpsBuffer;
    private int fpsBufferIndex;

    void InitializeBuffer()
    {
        if (frameRange <= 0)
        {
            frameRange = 1;
        }

        fpsBuffer = new int[frameRange];
        fpsBufferIndex = 0;
    }
    
    void Update()
    {
        if (fpsBuffer == null || fpsBuffer.Length != frameRange)
        {
            InitializeBuffer();
        }
        UpdateBuffer();
        CalculateFPS();
    }

    void UpdateBuffer()
    {
        fpsBuffer[fpsBufferIndex++] = (int) (1f / Time.unscaledDeltaTime);
        if (fpsBufferIndex >= frameRange)
        {
            fpsBufferIndex = 0;
        }
    }

    void CalculateFPS()
    {
        int sum = 0;
        for (int i = 0; i < frameRange; i++)
        {
            sum += fpsBuffer[i];
        }

        AverageFPS = sum / frameRange;
    }
}
```

现在，需求又变了，我们还想增加显示一下这段时间内的最高帧数与最低帧数：

```c#
//FPSCounter.cs
  public int HighestFPS { get; private set; }
	public int LowestFPS { get; private set; }

	void CalculateFPS () {
		int sum = 0;
		int highest = 0;
		int lowest = int.MaxValue;
		for (int i = 0; i < frameRange; i++) {
			int fps = fpsBuffer[i];
			sum += fps;
			if (fps > highest) {
				highest = fps;
			}
			if (fps < lowest) {
				lowest = fps;
			}
		}
		AverageFPS = sum / frameRange;
		HighestFPS = highest;
		LowestFPS = lowest;
	}

//FPSDisplay.cs
	public Text highestFPSLabel, averageFPSLabel, lowestFPSLabel;

	void Update () {
		highestFPSLabel.text =
			stringsFrom00To99[Mathf.Clamp(fpsCounter.HighestFPS, 0, 99)];
		averageFPSLabel.text =
			stringsFrom00To99[Mathf.Clamp(fpsCounter.AverageFPS, 0, 99)];
		lowestFPSLabel.text =
			stringsFrom00To99[Mathf.Clamp(fpsCounter.LowestFPS, 0, 99)];
	}
```

最后，让这个Label有额外的颜色，而不是单调的黑白

我们在FPSDisplay.cs中引入一个私有类来在Unity Editor中引入Color和最小FPS进行控制（注意要使用序列化来暴露给Unity Editor），再引入对于这个私有类的数组来进行输入控制：

```c#
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(FPSCounter))]
public class FPSDisplay : MonoBehaviour
{
    public Text highestFPSLabel, averageFPSLabel, lowestFPSLabel;

    private FPSCounter fpsCounter;

    [SerializeField] private FPSColor[] coloring;

    //根据FPS改变颜色，FPSDisplay将是唯一使用它的类，所以直接在类中设定私有类，并且设定为可序列化以便暴露给Unity editor
    [System.Serializable]
    private struct FPSColor
    {
        public Color color;
        public int minimumFPS;
    }

    void Awake()
    {
        fpsCounter = GetComponent<FPSCounter>();
    }

    static string[] stringsFrom00To99 =
    {
        "00", "01", "02", "03", "04", "05", "06", "07", "08", "09",
        "10", "11", "12", "13", "14", "15", "16", "17", "18", "19",
        "20", "21", "22", "23", "24", "25", "26", "27", "28", "29",
        "30", "31", "32", "33", "34", "35", "36", "37", "38", "39",
        "40", "41", "42", "43", "44", "45", "46", "47", "48", "49",
        "50", "51", "52", "53", "54", "55", "56", "57", "58", "59",
        "60", "61", "62", "63", "64", "65", "66", "67", "68", "69",
        "70", "71", "72", "73", "74", "75", "76", "77", "78", "79",
        "80", "81", "82", "83", "84", "85", "86", "87", "88", "89",
        "90", "91", "92", "93", "94", "95", "96", "97", "98", "99"
    };

    void Update()
    {
        Display(highestFPSLabel, fpsCounter.HighestFPS);
        Display(averageFPSLabel, fpsCounter.AverageFPS);
        Display(lowestFPSLabel, fpsCounter.LowestFPS);
    }

    void Display(Text label, int fps)
    {
        label.text = stringsFrom00To99[Mathf.Clamp(fps, 0, 99)];
        for (int i = 0; i < coloring.Length; i++)
        {
            if (fps >= coloring[i].minimumFPS)
            {
                label.color = coloring[i].color;
                break;
            }
        }
    }
}
```

