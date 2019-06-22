## Unity 学习记录1——Clock

我们可以通过临时父类来控制总的旋转，然后再把子类取出来，这样子类依然会"记住"自己的位置，这样实现快速旋转设置的目的(如果只是调整子类的旋转，那么只会修改朝向，而无法改变位置)

众所周知，Unity的脚本主要使用C#进行控制，这里有几个点：

+ 当我们没有MonoBehavior的时候，我们是不能够通过这个创造组件的，而只是一个通用的类
+ Unity只能使用MonoBehaviour的子类型来创建组件
+ 但是如果只有MonoBehavior也不行，必须要有名字空间UnityEngine(在开头使用`using`声明，如下)，或者继承全称的子类(即`UnityEngine.MonoBehaviour`)

```c#
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Clock : UnityEngine.MonoBehaviour
{
	private const float degreesPerHour = 30f;//每1h旋转30°
	private const float degreesPerMinute = 6f;
	private const float degreesPerSecond = 6f;
	
	public Transform hoursTransform, minuteTransform, secondTransform;

	public bool continuous;
	
	private void Awake()
	{
		DateTime time = DateTime.Now;
		
		Debug.Log(time);//记录当前时间
		
		hoursTransform.localRotation = Quaternion.Euler(0f, time.Hour * degreesPerHour, 0f);
		minuteTransform.localRotation = Quaternion.Euler(0f,time.Minute * degreesPerMinute, 0f);
		secondTransform.localRotation = Quaternion.Euler(0f,time.Second * degreesPerSecond, 0f);
	}

	private void Update()
	{
		if (continuous)
		{
			UpdateContinuous();
		}
		else
		{
			UpdateDiscrete();
		}
	}

	void UpdateContinuous()
	{
		//DateTime time = DateTime.Now;
		TimeSpan time = DateTime.Now.TimeOfDay;//提供连续的旋转的摇臂
		
		//默认给的是double，unity不支持将会引发error，需要手动改成float
		hoursTransform.localRotation = Quaternion.Euler(0f, (float)time.TotalHours * degreesPerHour, 0f);
		minuteTransform.localRotation = Quaternion.Euler(0f, (float)time.TotalMinutes * degreesPerMinute, 0f);
		secondTransform.localRotation = Quaternion.Euler(0f, (float)time.TotalSeconds * degreesPerSecond, 0f);
	}

	void UpdateDiscrete()
	{
		DateTime time = DateTime.Now;
		hoursTransform.localRotation = Quaternion.Euler(0f, time.Hour * degreesPerHour, 0f);
		minuteTransform.localRotation = Quaternion.Euler(0f, time.Minute * degreesPerMinute, 0f);
		secondTransform.localRotation = Quaternion.Euler(0f, time.Second * degreesPerSecond, 0f);
	}
}
```



