##Unity 学习记录3

我们在前一个教程中完成了一个线图，在Play mode下可以完成正弦波等函数。

当我们在Play Mode中改变了Script 的代码，执行会被暂停，当前游戏状态保留，脚本将会被重新编译，最后游戏状态将会被reload and Play resumes。但是并不是所有部分都会在Play Mode中热加载更新，但是我们的graph的确是重编译更新了，它改变了动画的函数。

但是这事实上并不方便，更好的方式是我们可以简化称为我们只需要改变一下config选项就能够让graph按照我们的预期进行。

我们首先就是将其抽象化为一个个独立的函数，使得逻辑更加清晰。此外，为了使我们的graph同时支持多个函数，我们必须将所有函数编程到其中。但是，循环遍历图形点的代码并不关心使用哪个函数。所以我们可以借此进行抽象化

此外，尽量暴露出函数的可调整接口，使得整体工作更加易于配置

使用静态的方法能够提高性能：当我们写的代码中，如果不需要访问任何其他的方法或者字段，也就证明我们把它放在任何一个类中都是可以的，我们可以为其制造静态方法处理之

```C#
	static float SineFunction (float x, float t) {
		return Mathf.Sin(Mathf.PI * (x + t));
	}

	static float MultiSineFunction (float x, float t) {
		float y = Mathf.Sin(Mathf.PI * (x + t));
		y += Mathf.Sin(2f * Mathf.PI * (x + 2f * t)) / 2f;
		y *= 2f / 3f;
		return y;
	}
```

这些方法仍然是Graph的一部分，但它们现在直接与类类型相关联，不再绑定到对象实例上。

#### 委托(Delegate)

现在我们只是支持了简单的if-else进行选择，但是如果是更多函数需要支持呢？吐过我们能够使用变量来存储对于我们要调用的方法的引用，无疑是更好的。

委托是一个特殊的类型，该类型定义了什么方法能够被引用。我们的数学函数没有标准的类型，但是我们可以自己定义一个。我们为了创建它，建立一个新的C# Script Asset，称为`GraphFunction`(也可以写在原来的`Graph`里面，但是这样最后文件会过于臃肿，最好进行像这样的合理分割)

对于这个委托的C# 文件，我们需要定义名为GraphFunction的公共委托类型。这与类或结构定义不同，必须后跟分号。

```c#
using UnityEngine;
public delegate float GraphFunction (float x, float t);

//Graph.cs
GraphFunction f;
if (function == 0)
{
  f = SineFunction;//使用委托函数
}
else
{
  f = MultiSineFunction;
}
for (int i = 0; i < points.Length; i++)
```

Trick：这里可以使用委托数组的方式

```c#
static GraphFunction[] functions = {SineFunction, MultiSineFunction};

private void Update()
{
  float t = Time.time;
  GraphFunction f = functions[function];//function是外界引入的public参数
  //...
```

####枚举策略：

我们之前使用了

```c#
[Range(0, 1)] public int function;
```

在Unity中显示为整数滑块，但0表示正弦函数，1表示多正弦函数并不明显。如果我们有一个包含有意义名称的下拉列表，那就更清楚了。我们可以使用枚举来实现这一点。

可以通过定义枚举类型来创建枚举。创建一个新的C# Script Asset以包含此类型，名为GraphFunctionName。然后在其中给出了枚举列表

```c#
public enum GraphFunctionName
{
    Sine,
    MultiSine
}
```

在Graph.cs中去掉之前的滑块设计，加入枚举列表的设计

```c#
    public GraphFunctionName function;

//在Update()中要使用强制类型转换
		GraphFunction f = functions[(int)function];
```

### 增加到3维

我们现在从y = f(x)拓展到3维空间，我们首先需要改变委托函数，并且在使用委托函数的地方都进行合理修改，加上z这个变量

```C#
public delegate float GraphFunction (float x, float z, float t);

static float SineFunction (float x, float z, float t) {
  return Mathf.Sin(Mathf.PI * (x + t));
}

static float MultiSineFunction (float x, float z, float t) {
  float y = Mathf.Sin(Mathf.PI * (x + t));
  y += Mathf.Sin(2f * Mathf.PI * (x + 2f * t)) / 2f;
  y *= 2f / 3f;
  return y;
}

position.y = f(position.x, position.z, t);

```

接下来创建网格原点，为了显示Z维度，我们必须将我们的"点线"转换为"点网格"。我们可以通过创建多条线来实现这一点，每条线沿Z方向偏移一步。我们将使用与X相同的Z范围，因此我们将创建与当前点数相同的线条。这意味着我们必须平衡点数。在Awake中调整点数组的创建，使其足以包含所有点：

```c#
points = new Transform[resolution * resolution];
```

当我们根据分辨率增加每次迭代的X坐标时，只需创建更多的点就会产生一条长线。我们必须调整初始化循环以考虑第二个维度。

在Awake函数中，我们构建了一个一系列沿着Z轴延伸的"棒"，后续的Update函数中，我们对每个point进行对y轴的周期偏振，也就控制了每个"棒"的行为。

```C#
for (int i = 0, z = 0; z < resolution; z++) {
  position.z = (z + 0.5f) * step - 1f;
  for (int x = 0; x < resolution; x++, i++) {
    Transform point = Instantiate(pointPrefab);
    position.x = (x + 0.5f) * step - 1f;
    point.localPosition = position;
    point.localScale = scale;
    point.SetParent(transform, false);
    points[i] = point;
  }
}
```

现在我们增加一个函数(三维)

```c#
//f(x,z,t) = (sin(pi(x+t)) + sin(pi(z+t))) / 2
//有点布的效果
static float Sine2DFunction(float x, float z, float t)
{
  //return (float)Math.Sin(pi * (x + t + z));
  float y = Mathf.Sin(pi * (x + t));
  y += Mathf.Sin(pi * (z + t));
  y *= 0.5f;//使用乘法指令能够使得计算更快
  return y;
}

//同时需要改变委托数组等名称等
static GraphFunction[] functions = {SineFunction, Sine2DFunction, MultiSineFunction};

//GraphFunctionName.cs
public enum GraphFunctionName
{
    Sine,
    Sine2D,
    MultiSine
}
```

现在就是增加我们数学图形表示库的时候了，我们这里选择尝试建造一个波纹(Ripple)

```c#
static float Ripple(float x, float z, float t)
{
  float d = Mathf.Sqrt(x * x + z * z);//d代表距离
  float y = Mathf.Sin(pi * (4f * d - t));//4f来控制频率，t来动画，因为波动是向外延展的，所以进行的是减法
  y /= 1f + 10f * d;//除以距离来控制波动的幅度，加上1是因为避免在原点时因为除以0导致波动巨大
  return y;
}
```

#### 隐函数

通过使用X和Z来定义Y，我们可以创建描述各种曲面的函数，但它们始终链接到XZ平面。没有两个点可以具有相同的X和Z坐标，同时具有不同的Y坐标。这意味着我们表面的曲率是有限的。它们的斜坡不能垂直，不能向后折叠。为了实现这一点，我们的功能不仅要输出Y，还要输出X和Z。

**新方法：用自己定义的变量u，v来控制三维空间的x，y，z；**即类似于：

$f(u, v)=\left[\begin{array}{c}{u+v} \\ {u v} \\ {\frac{u}{v}}\end{array}\right]$

现在，调整我们的GraphFunction委托以支持这种新方法。唯一需要的更改是用Vector3替换它的float返回类型，但是我们最好重命名它的参数来避免误解。

```c#
public delegate Vector3 GraphFunction (float u, float v, float t);
```

然后重构Awake等代码

```c#
//例如对于Ripple的重构
static Vector3 Ripple (float x, float z, float t) {
  Vector3 p;
  float d = Mathf.Sqrt(x * x + z * z);
  p.x = x;
  p.y = Mathf.Sin(pi * (4f * d - t));
  p.y /= 1f + 10f * d;
  p.z = z;
  return p;
}

//重构Awake代码
	void Awake () {
		float step = 2f / resolution;
		Vector3 scale = Vector3.one * step;
//		Vector3 position;
//		position.y = 0f;
//		position.z = 0f;
		points = new Transform[resolution * resolution];
//		for (int i = 0, z = 0; z < resolution; z++) {
//			position.z = (z + 0.5f) * step - 1f;
//			for (int x = 0; x < resolution; x++, i++) {
//				Transform point = Instantiate(pointPrefab);
//				position.x = (x + 0.5f) * step - 1f;
//				point.localPosition = position;
//				point.localScale = scale;
//				point.SetParent(transform, false);
//				points[i] = point;
//			}
//		}
		for (int i = 0; i < points.Length; i++) {
			Transform point = Instantiate(pointPrefab);
			point.localScale = scale;
			point.SetParent(transform, false);
			points[i] = point;
		}
	}

//重构Update代码
	void Update () {
		float t = Time.time;
		GraphFunction f = functions[(int)function];
//		for (int i = 0; i < points.Length; i++) {
//			Transform point = points[i];
//			Vector3 position = point.localPosition;
//			position.y = f(position.x, position.z, t);
//			point.localPosition = position;
//		}
		float step = 2f / resolution;
		for (int i = 0, z = 0; z < resolution; z++) {
			float v = (z + 0.5f) * step - 1f;
			for (int x = 0; x < resolution; x++, i++) {
				float u = (x + 0.5f) * step - 1f;
				points[i].localPosition = f(u, v, t);
			}
		}
	}
```

现在，我们可以用这个来建立一个圆柱体啦

```c#
static Vector3 Cylinder (float u, float v, float t) {
  Vector3 p;
  float r = 1f;//控制缩放半径
  p.x = r * Mathf.Sin(pi * u);
  p.y = v;
  p.z = r * Mathf.Cos(pi * u);
  return p;
}

//甚至r可以不是const
//这里可以设定R = 1 + sin(2pi*u)/5
float r = 1f + Mathf.Sin(6f * pi * u) * 0.2f;//出现6片花瓣状
//当把上面的u换成v之后，这个圆柱体会随着高度半径出现周期性变化

//如果把上面两个混合起来，会出现一个6片花瓣状的“圆柱体”从上到下出现横向扭曲，同时开始旋转(呈螺旋状)
float r = 0.8f + Mathf.Sin(pi * (6f * u + 2f * v + t)) * 0.2f;

```

接下来，我们用我们的数学公式可以创建一个Sphere了

```c#
static Vector3 Sphere(float u, float v, float t)
{
  Vector3 p;
  float r = Mathf.Cos(pi * 0.5f * v);
  p.x = r * Mathf.Sin(pi * u);
  p.y = Mathf.Sin(pi * 0.5f * v);//与r形成勾股定理构成圆
  p.z = r * Mathf.Cos(pi * u);
  return p;
}
```

但是虽然此方法创建了正确的球体，但请注意点的分布不均匀，因为球体是通过堆叠具有不同半径的圆形来创建的。在球体的两极，它们的半径变为零。

为了控制半径，可以使用这样的函数
$$
f(u, v)=\left[\begin{array}{l}{S \sin (\pi u)} \\ {R \sin \left(\frac{\pi v}{2}\right)} \\ {S \cos (\pi u)}\end{array}\right] \text { where } S=R \cos \left(\frac{\pi v}{2}\right) \text{ and R is the radius}
$$
我们设置R与变量u，v都相关
$$
R=\frac{4}{5}+\frac{\sin (\pi(6 u+t))}{10}+\frac{\sin (\pi(4 v+t))}{10}
$$

```c#
float r = 0.8f + Mathf.Sin(pi * (6f * u + t)) * 0.1f;
r += Mathf.Sin(pi * (4f * v + t)) * 0.1f;
float s = r * Mathf.Cos(pi * 0.5f * v);
p.x = s * Mathf.Sin(pi * u);
p.y = r * Mathf.Sin(pi * 0.5f * v);
p.z = s * Mathf.Cos(pi * u);
```

我们再向前一步，做一个圆环：

想法很简单，把一个球体拉开就行，即

```c#
static Vector3 Torus (float u, float v, float t) {
  Vector3 p;
  float s = Mathf.Cos(pi * 0.5f * v) + 0.5f;//0.5f的拉开
  p.x = s * Mathf.Sin(pi * u);
  p.y = Mathf.Sin(pi * 0.5f * v);
  p.z = s * Mathf.Cos(pi * u);
  return p;
}
```

但是到这里只是完成了外表面，内表面被撕裂开的地方也需要处理，修改上面代码为：

```c#
		float s = Mathf.Cos(pi * v) + 0.5f;
		p.x = s * Mathf.Sin(pi * u);
		p.y = Mathf.Sin(pi * v);
		p.z = s * Mathf.Cos(pi * u);
```

我们将球体拉开了半个单位，这就形成了一个自相交的形状，这就是所谓的主轴圆环(spindle torus)。如果我们把它拆开一个单位，我们就会得到一个不会自相交的圆环，但也没有一个洞，这就是所谓的角环(horn torus)。那么我们拉开球体的距离会影响圆环的形状。具体来说，它定义了圆环的主半径，我们将指定它为R1

即函数
$$
f(u, v)=\left[\begin{array}{c}{S \sin (\pi u)} \\ {\sin (\pi v)} \\ {S \cos (\pi u)}\end{array}\right] \text { where } S=\cos (\pi v)+R_{1}
$$

```c#
		float r1 = 1f;
		float s = Mathf.Cos(pi * v) + r1;
```

这里存在一个先验条件，

制造R1大于1将在圆环的中间打开一个孔，这将使其成为圆环。但是这里假设我们围绕环的圆总是具有半径1。这是圆环的次半径定义为R2，我们也可以改变这个R2：
$$
f(u, v)=\left[\begin{array}{c}{S \sin (\pi u)} \\ {R_{2} \sin (\pi v)} \\ {S \cos (\pi u)}\end{array}\right] \text { where } S=R_{2} \cos (\pi v)+R_{1}
$$
写成C#则为：

```c#
float r1 = 1f;
float r2 = 0.5f;
float s = r2 * Mathf.Cos(pi * v) + r1;
p.x = s * Mathf.Sin(pi * u);
p.y = r2 * Mathf.Sin(pi * v);
p.z = s * Mathf.Cos(pi * u);
```

这时，我们真正得到了符合我们日常认识的"圆环"

综合之前的一些特效，我们可以只要稍微改动一下r，就会有很有趣的新特效：

```c#
float r1 = 0.65f + Mathf.Sin(pi * (6f * u + t)) * 0.1f;
float r2 = 0.2f + Mathf.Sin(pi * (4f * v + t)) * 0.05f;
```