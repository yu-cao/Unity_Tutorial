## Unity 学习记录2

Prefab（预制件）——把它从Hierarchy层拉到Project层即可产生该东西的预制件

预制件是配置游戏对象的便捷方式。如果更改prefab asset，则任何场景中的所有实例都将以相同方式更改。例如，更改预制件的比例也将更改仍在场景中的立方体的比例。但是，每个实例都使用自己的位置和旋转。此外，游戏对象可以修改其属性，从而覆盖预制件的值。如果进行了大的更改，例如添加或删除组件，则预制件和实例之间的关系将被破坏。

我们可以使用脚本来创建预制件的实例，并且可以在最后绑定到`transform[]`中进行处理，完成对于每个实例的控制

同时可以通过`setParent`函数来确定父节点，进行绑定来整体控制实例化物体

```c#
using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

public class Graph : MonoBehaviour
{
    public Transform pointPrefab;
    [Range(10,100)]public int resolution = 10;
    private Transform[] points;//使得我们的画面动起来的控制数组
    
    private void Awake()
    {
        float step = 2f / resolution;
        Vector3 scale = Vector3.one * step;//控制局部比例 2/10 = 1/5
        Vector3 position;
        position.z = 0f;
        
        points = new Transform[resolution];
        
        for (int i = 0; i < resolution; i++)
        {
            Transform point = Instantiate(pointPrefab); 
            position.x = (i + 0.5f) * step - 1f;
            position.y = position.x * position.x * position.x;

            point.localPosition = position;
            point.localScale = scale;
            
            //设置新父级时，Unity将尝试将对象保持在其原始世界pos,rotation,scale。在我们的例子中，我们不需要那样做。
            //我们可以通过提供false作为SetParent的第二个参数来发出信号。
            point.SetParent(transform, false);//设定父节点

            points[i] = point;//把实例化的顶点绑定到数组中来
        }
    }

    private void Update()
    {
        for (int i = 0; i < points.Length; i++)
        {
            Transform point = points[i];
            Vector3 position = point.localPosition;
            position.y = Mathf.Sin(Mathf.PI * (position.x + Time.time));//进行正弦曲线运动
            point.localPosition = position;
        }
    }
}
```

