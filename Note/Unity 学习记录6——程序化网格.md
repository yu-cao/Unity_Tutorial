## Unity学习记录——程序化生成网格

我们知道，在Unity中看到一些东西，需要一个mesh。我们可以程序化生成mesh，这可以是sprite，UI element或者particle system。

如果想让一个Game object显示一个3D模型，需要两个组件：`mesh filter`和`mesh renderer`。前者包含对要显示的mesh的引用，后者配置网格的渲染方式（控制材质，阴影等）

mesh renderer可以包含多种material。这主要用于渲染具有多个独立的三角形集的网格，称为子网格。这些主要用于导入的3D模型。

我们可以通过给我们的mesh提供albedo map的方式增加大量的细节，比如直接把一个texture放上去就行

现在，我们开始创造顶点网格

定义一个有长和宽大小的组件，并将其绑定到一个空的GameObject上：

```c#
sing UnityEngine;
using System.Collections;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class Grid : MonoBehaviour {

	public int xSize, ySize;
}
```

然后我们创建顶点数组：

```c#
    private void Generate()
    {
        //注意：顶点每行/列顶点数应该比行/列数多1（比如1行必然有上下2个顶点）
        vertices = new Vector3[(xSize + 1) * (ySize + 1)];
        for (int i = 0, y = 0; y <= ySize; y++) {
          for (int x = 0; x <= xSize; x++, i++) {
            vertices[i] = new Vector3(x, y);
          }
        }
    }

    private void Awake()
    {
        Generate();
    }

    //可视化这些顶点，用小黑球代替
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.black;
        for (int i = 0; i < vertices.Length; i++)
        {
            Gizmos.DrawSphere(vertices[i],0.1f);
        }
    }
```

这里用到了一个新的概念：`gizmos`。这是可以在编辑器中使用的视觉提示。默认情况下，它们**在Scene View中可见，而在Game View中不可见**，但可以通过其工具栏进行调整。 Gizmos utility让我们可以绘制icons，lines等等。Gizmos可以在OnDrawGizmos方法中绘制，该方法由Unity Editor自动调用。另一种方法是OnDrawGizmosSelected，它仅针对所选对象调用。（此外，gizmos是直接生成在世界空间中，我们可以显式指定`transform.TransformPoint(vertices[i])`来替换`vertices[i]`进行本地空间的定义）

我们可以用协程来减慢生成速度进行观察：

```c#
	private void Awake () {
		StartCoroutine(Generate());
	}

	private IEnumerator Generate () {
		WaitForSeconds wait = new WaitForSeconds(0.05f);
		vertices = new Vector3[(xSize + 1) * (ySize + 1)];
		for (int i = 0, y = 0; y <= ySize; y++) {
			for (int x = 0; x <= xSize; x++, i++) {
				vertices[i] = new Vector3(x, y);
				yield return wait;
			}
		}
	}
```

至此，顶点我们已经生成完毕了，我们开始创建Mesh

```c#
	private Mesh mesh;

	private IEnumerator Generate () {
		WaitForSeconds wait = new WaitForSeconds(0.05f);
		
		GetComponent<MeshFilter>().mesh = mesh = new Mesh();
		mesh.name = "Procedural Grid";

		vertices = new Vector3[(xSize + 1) * (ySize + 1)];
		…
		mesh.vertices = vertices;
	}
```

现在我们在Play mode中会有一个mesh，但是没有任何显示，因为我们没有给它任何三角形，三角形通过顶点索引数组进行定义，3个连续的索引描述一个三角形

```c#
	private IEnumerator Generate () {
		…

		int[] triangles = new int[3];
		triangles[0] = 0;
		triangles[1] = xSize + 1;
		triangles[2] = 1;
		mesh.triangles = triangles;
	}
```

这里有一个三角形可见性问题：三角形的哪一侧可见由其顶点索引的方向决定。默认情况下，如果它们按顺时针方向排列，则三角形被认为是面向前方且可见的。逆时针三角形被丢弃，最后可以得到这样的代码：

```c#
	private void Awake () {
		Generate();
	}

	private void Generate () {
		GetComponent<MeshFilter>().mesh = mesh = new Mesh();
		mesh.name = "Procedural Grid";

		vertices = new Vector3[(xSize + 1) * (ySize + 1)];
		for (int i = 0, y = 0; y <= ySize; y++) {
			for (int x = 0; x <= xSize; x++, i++) {
				vertices[i] = new Vector3(x, y);
			}
		}
		mesh.vertices = vertices;

		int[] triangles = new int[xSize * ySize * 6];
		for (int ti = 0, vi = 0, y = 0; y < ySize; y++, vi++) {
			for (int x = 0; x < xSize; x++, ti += 6, vi++) {
				triangles[ti] = vi;
				triangles[ti + 3] = triangles[ti + 2] = vi + 1;
				triangles[ti + 4] = triangles[ti + 1] = vi + xSize + 1;
				triangles[ti + 5] = vi + xSize + 2;
			}
		}
		mesh.triangles = triangles;
	}
```

接下来我们要创建额外的顶点

我们因为没有法线导致整体的光照感非常奇怪，默认法线的方向是(0,0,1)，正好与我们所需要的法线方向相反，所以我们在这里需要重新计算法线（法线是按照顶点定义的，所以我们要填充另一个矢量数组，可以根据网格的三角形找出法线本身）我们可以偷懒使用Unity内置的算法：

```c#
	private void Generate () {
		…
		mesh.triangles = triangles;
		mesh.RecalculateNormals();//内置函数
	}
```

接下来是UV坐标，要使纹理适合我们的整个网格，只需将顶点的位置除以网格尺寸即可。

```c#
		vertices = new Vector3[(xSize + 1) * (ySize + 1)];
		Vector2[] uv = new Vector2[vertices.Length];
		for (int i = 0, y = 0; y <= ySize; y++) {
			for (int x = 0; x <= xSize; x++, i++) {
				vertices[i] = new Vector3(x, y);
				uv[i] = new Vector2(x / xSize, y / ySize);
			}
		}
		mesh.vertices = vertices;
		mesh.uv = uv;
```

这里会发现，为什么uv没有平铺到整个mesh上？发生这种情况是因为我们当前正在用整数划分整数，从而产生另一个整数。要在整个网格中获得0和1之间的正确坐标，我们必须确保使用浮点数。即改成`uv[i] = new Vector2((float)x / xSize, (float)y / ySize);`同时合理修改Tilling和Offset就可以满足我们的想法

我们也可以使用法线贴图来增加细节

```c#
		vertices = new Vector3[(xSize + 1) * (ySize + 1)];
		Vector2[] uv = new Vector2[vertices.Length];
		Vector4[] tangents = new Vector4[vertices.Length];
		Vector4 tangent = new Vector4(1f, 0f, 0f, -1f);
		for (int i = 0, y = 0; y <= ySize; y++) {
			for (int x = 0; x <= xSize; x++, i++) {
				vertices[i] = new Vector3(x, y);
				uv[i] = new Vector2((float)x / xSize, (float)y / ySize);
				tangents[i] = tangent;
			}
		}
		mesh.vertices = vertices;
		mesh.uv = uv;
		mesh.tangents = tangents;
```

法线贴图在切线空间中定义。这种方法允许我们在不同的地方和方向应用相同的法线贴图。切线是3D矢量，但Unity实际上使用4D矢量。它的第四个分量始终为-1或1，用于控制第三个切线空间尺寸的方向 - 向前或向后。这有利于法线贴图的镜像，法线贴图通常用于具有双边对称性的事物的3D模型，如人。 Unity的着色器执行此计算的方式要求我们使用-1。