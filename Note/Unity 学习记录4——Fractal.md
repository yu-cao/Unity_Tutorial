##Unity学习记录4

本文将主要讲Unity中分形(Fractal)的知识

一个分形的details跟它的整体长得完全一样。我们可以把它应用于Unity中的对象层次结构中。从一些根对象开始，然后添加较小但相同的子对象。手动执行此操作会非常麻烦，因此我们将创建一个脚本来为我们执行此操作

```c#
using UnityEngine;
using System.Collections;

public class Fractal : MonoBehaviour {
	
	public Mesh mesh;
	public Material material;

	private void Start () {
		//AddComponent函数将会创建某个类型的新组件，将其附加在游戏对象上，并返回其引用
		gameObject.AddComponent<MeshFilter>().mesh = mesh;
		gameObject.AddComponent<MeshRenderer>().material = material;
	}
}
```

接下来，我们创建分形的子分形，一个最简单的方法是在上面Awake代码中的后面加一行：

```c#
		new GameObject("Fractal Child").AddComponent<Fractal>();
```

但是我们很容易发现，这个构造是个不会停止的递归，这虽然很慢，但是最终会必定会导致内存泄漏。

为此，我们一定要定义递归深度，同时我们不可能手动调整子节点的各种材质，这里我们选择与父节点相同，即改成（额外增加一个初始化函数）：

```c#
    public Mesh mesh;
    public Material material;
    public int maxDepth;

    private int depth;

    private void Start()
    {
        gameObject.AddComponent<MeshFilter>().mesh = mesh;
        gameObject.AddComponent<MeshRenderer>().material = material;
        if (depth < maxDepth)
        {
            //this引用了其方法被调用的当前对象或结构，这里我们正在调用新子对象的Initialize方法，而不是父对象
            new GameObject("Fractal Child").AddComponent<Fractal>().Initialize(this);
        }
    }

    private void Initialize(Fractal parent)
    {
        mesh = parent.mesh;
        material = parent.material;
        maxDepth = parent.maxDepth;
        depth = parent.depth + 1;
        transform.parent = parent.transform;//指定父节点以在Hierarchy中真正定义出层级结构
    }
```

现在已经了解了如何在原理上去创造子节点，接下来开始真正塑造子节点

之前的代码，我们的Child跟它们的Parent在同一个位置上，而且没有Scale的变化，所以我们看到的都是一个Cube，接下来我们就来改变它们

```c#
public int childScale;

//在Initialize函数中加上
//调整位置和大小
childScale = parent.childScale;
transform.localScale = Vector3.one * childScale;
transform.localPosition = Vector3.up * (0.5f + 0.5f * childScale);
```

接下来，我们就从1个Child延伸到多个Child

```c#
if (depth < maxDepth)
{
    new GameObject("Fractal Child").AddComponent<Fractal>().Initialize(this, Vector3.up);
    new GameObject("Fractal Child").AddComponent<Fractal>().Initialize(this, Vector3.right);
}

private void Initialize(Fractal parent, Vector3 direction)
{
    ...
    transform.localPosition = direction * (0.5f + 0.5f * childScale);
}
```

接下来，我们想用协程的方式完成这个过程，这里解释一下IEnumerator和yield：

+ `IEnumerator`这东西是一个item go through一些collection的概念，就像循环中的迭代器的遍历。协程需要使用这个东西
+ `yield`使用于迭代器上以进行简化，为了使得枚举的实现，我们需要跟踪进度，而这涉及到一些样板代码，这些代码是必要但是完全一样的。而我们只是想写一些例如return firstItem; return secondItem;这种直到我们完成整个过程。而当我们使用`yield`时候，一个枚举器对象会被隐式创建来处理一些东西，这也就是为什么我们的返回值是`IEnumerator`的原因

```c#
private void Start()
{
  gameObject.AddComponent<MeshFilter>().mesh = mesh;
  gameObject.AddComponent<MeshRenderer>().material = material;
  if (depth < maxDepth)
  {
    StartCoroutine(CreateChildren());
  }
}

private IEnumerator CreateChildren()
{
  yield return new WaitForSeconds(0.5f);
  new GameObject("Fractal Child").AddComponent<Fractal>().Initialize(this, Vector3.up);
  yield return new WaitForSeconds(0.5f);
  new GameObject("Fractal Child").AddComponent<Fractal>().Initialize(this, Vector3.right);
  yield return new WaitForSeconds(0.5f);
  new GameObject("Fractal Child").AddComponent<Fractal>().Initialize(this, Vector3.left);
}
```

实际上，当我们在Unity中创建一个协程时，我们实际只是在创建一个迭代器。当它被传递给`StartCoroutine`方法时，它会被存储并且被要求每帧移向下一个item直到完成。而`yield`语句就是产生这些items，我们可以产生一些特殊的东西，比如WaitForSeconds，以便更好地控制自己的代码何时继续，但事实上只是一个迭代器。

我们增加了上、右、左这三个延展方向，但是我们发现，我们没有规定方向，也就是说，一个显然的问题是：向左延伸的Child，它的向右的child会直接生成在祖父物体的内部，这显然不是我们的预期，我们要引入方向的概念(四元数)

```c#
	private IEnumerator CreateChildren () {
		yield return new WaitForSeconds(0.5f);
		new GameObject("Fractal Child").AddComponent<Fractal>().
			Initialize(this, Vector3.up, Quaternion.identity);
		yield return new WaitForSeconds(0.5f);
		new GameObject("Fractal Child").AddComponent<Fractal>().
			Initialize(this, Vector3.right, Quaternion.Euler(0f, 0f, -90f));
		yield return new WaitForSeconds(0.5f);
		new GameObject("Fractal Child").AddComponent<Fractal>().
			Initialize(this, Vector3.left, Quaternion.Euler(0f, 0f, 90f));
	}
	
	private void Initialize (Fractal parent,
	                         Vector3 direction,
	                         Quaternion orientation) {
		…
		transform.localRotation = orientation;
	}
```

优化代码结构：

```c#
	private static Vector3[] childDirections = {
		Vector3.up,
		Vector3.right,
		Vector3.left
	};

	private static Quaternion[] childOrientations = {
		Quaternion.identity,
		Quaternion.Euler(0f, 0f, -90f),
		Quaternion.Euler(0f, 0f, 90f)
	};

	private IEnumerator CreateChildren () {
		for (int i = 0; i < childDirections.Length; i++) {
			yield return new WaitForSeconds(0.5f);
			new GameObject("Fractal Child").AddComponent<Fractal>().
				Initialize(this, i);
		}
	}

	private void Initialize (Fractal parent, int childIndex) {
		…
		transform.localPosition =
			childDirections[childIndex] * (0.5f + 0.5f * childScale);
		transform.localRotation = childOrientations[childIndex];
	}
```

接下来，我们想引入颜色，这里我们使用插值法进行颜色生成，从白色变成黄色，但是这样的处理会破坏动态批处理的流程。

```c#
	private void Start () {
		gameObject.AddComponent<MeshFilter>().mesh = mesh;
		gameObject.AddComponent<MeshRenderer>().material = material;
		GetComponent<MeshRenderer>().material.color =
			Color.Lerp(Color.white, Color.yellow, (float)depth / maxDepth);
		if (depth < maxDepth) {
			StartCoroutine(CreateChildren());
		}
	}
```

动态批处理只会在material完全相同时才会进行，这里Unity无法从语义上分析得到material的相同性，所以动态批处理被关闭，修改如下，使用material数组的方式将会让Unity知道我们调用的material是否是完全相同的（修改前setPass：1567 修改后setPass：17）：

```c#
	private Material[] materials;

	private void InitializeMaterials () {
		materials = new Material[maxDepth + 1];
		for (int i = 0; i <= maxDepth; i++) {
			materials[i] = new Material(material);
			materials[i].color =
				Color.Lerp(Color.white, Color.yellow, (float)i / maxDepth);
		}
	}
	
	private void Start () {
		if (materials == null) {
			InitializeMaterials();
		}
		gameObject.AddComponent<MeshFilter>().mesh = mesh;
		gameObject.AddComponent<MeshRenderer>().material = materials[depth];//使用material[]方式
		if (depth < maxDepth) {
			StartCoroutine(CreateChildren());
		}
	}

	private void Initialize (Fractal parent, int childIndex) {
		mesh = parent.mesh;
		materials = parent.materials;
		…
	}
```

注意：这里对于`material == null`的判断建立在material是一个私有的数组上，如果是`public`，Unity的序列化系统会自动为它创建一个空数组

然后可以很自然地按需求拓展变成二维material数组，实现更多颜色。

随机化网格

同理可以使用mesh数组来随机化网格

```c#
	public Mesh[] meshes;
	
	private void Start () {
		if (materials == null) {
			InitializeMaterials();
		}
		gameObject.AddComponent<MeshFilter>().mesh =
			meshes[Random.Range(0, meshes.Length)];
		gameObject.AddComponent<MeshRenderer>().material =
			materials[depth, Random.Range(0, 2)];
		if (depth < maxDepth) {
			StartCoroutine(CreateChildren());
		}
	}

	private void Initialize (Fractal parent, int childIndex) {
		meshes = parent.meshes;
		…
	}
```

Fractal 不规则化

我们的分形太完整了，完整得不像是个真的，所以我们需要引入一些随机的消除来更像真的一点

```c#
	public float spawnProbability;

	private IEnumerator CreateChildren () {
		for (int i = 0; i < childDirections.Length; i++) {
			if (Random.value < spawnProbability) {
				yield return new WaitForSeconds(Random.Range(0.1f, 0.5f));
				new GameObject("Fractal Child").AddComponent<Fractal>().
					Initialize(this, i);
			}
		}
	}

	private void Initialize (Fractal parent, int childIndex) {
		…
		spawnProbability = parent.spawnProbability;
		…
	}
```

最后，我们想让它动起来，

```c#
	private void Update () {
		transform.Rotate(0f, 30f * Time.deltaTime, 0f);
	}
```

再增加一点复杂度吧！我们引入扭曲（Twist）效果：通过添加微妙的旋转来敲击分形的元素而不对齐，表现出在表面翻滚的感觉

```c#
	public float maxTwist;

	private void Start () {
		rotationSpeed = Random.Range(-maxRotationSpeed, maxRotationSpeed);
		transform.Rotate(Random.Range(-maxTwist, maxTwist), 0f, 0f);
		…
	}

	private void Initialize (Fractal parent, int childIndex) {
		…
		maxTwist = parent.maxTwist;
		…
	}
```

