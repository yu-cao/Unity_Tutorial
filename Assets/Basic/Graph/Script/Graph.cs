using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

public class Graph : MonoBehaviour
{
    public Transform pointPrefab;
    [Range(10, 100)] public int resolution = 10;
    //[Range(0, 1)] public int function;
    public GraphFunctionName function;
    private Transform[] points; //使得我们的画面动起来的控制数组

    //使用原来的显函数的Awake
//    private void Awake()
//    {
//        float step = 2f / resolution;
//        Vector3 scale = Vector3.one * step; //控制局部比例 2/10 = 1/5
//        Vector3 position;
//        position.y = 0f;
//
//        //points = new Transform[resolution];
//        points = new Transform[resolution * resolution];
//
//        for (int i = 0, z = 0; z < resolution; z++) {
//            position.z = (z + 0.5f) * step - 1f;
//            for (int x = 0; x < resolution; x++, i++) {
//                Transform point = Instantiate(pointPrefab);
//                position.x = (x + 0.5f) * step - 1f;
//                point.localPosition = position;
//                point.localScale = scale;
//                point.SetParent(transform, false);
//                points[i] = point;
//            }
//        }
////        for (int i = 0; i < resolution; i++)
////        {
////            Transform point = Instantiate(pointPrefab);
////            position.x = (i + 0.5f) * step - 1f;
////            position.y = position.x * position.x * position.x;
////
////            point.localPosition = position;
////            point.localScale = scale;
////
////            //设置新父级时，Unity将尝试将对象保持在其原始世界pos,rotation,scale。在我们的例子中，我们不需要那样做。
////            //我们可以通过提供false作为SetParent的第二个参数来发出信号。
////            point.SetParent(transform, false); //设定父节点
////
////            points[i] = point; //把实例化的顶点绑定到数组中来
////        }
//    }

    //使用隐函数的Awake
    void Awake()
    {
        float step = 2f / resolution;
        Vector3 scale = Vector3.one * step;

        points = new Transform[resolution * resolution];

        for (int i = 0; i < points.Length; i++)
        {
            Transform point = Instantiate(pointPrefab);
            point.localScale = scale;
            point.SetParent(transform, false);
            points[i] = point;
        }
    }
    
    //使用委托数组的方式进行
    static GraphFunction[] functions =
    {
        SineFunction, Sine2DFunction, MultiSineFunction,
        MultiSine2DFunction, Ripple, Cylinder, Sphere, 
        Torus, SpindleTorus, InterestTorus
    };
    
    private void Update()
    {
        float t = Time.time;
        GraphFunction f = functions[(int)function];//必须要进行显式类型转换来满足枚举这个语法糖
        //用显函数来描述y
//        for (int i = 0; i < points.Length; i++)
//        {
//            Transform point = points[i];
//            Vector3 position = point.localPosition;
// 
//            position.y = f(position.x, position.z, t);
//            point.localPosition = position;
//        }

        //隐函数进行描述
        float step = 2f / resolution;
        for (int i = 0, z = 0; z < resolution; z++)
        {
            float v = (z + 0.5f) * step - 1f;
            for (int x = 0; x < resolution; x++, i++)
            {
                float u = (x + 0.5f) * step - 1f;
                points[i].localPosition = f(u, v, t);
            }
        }
    }

    const float pi = Mathf.PI;

    //使用显函数进行的运算
//    //f(x,t) = sin(pi(x+t))
//    static float SineFunction(float x, float z, float t)
//    {
//        return Mathf.Sin(pi * (x + t));
//    }
//
//    //f(x,t) = sin(pi(x+t)) + sin(2*pi(x+t))/2
//    static float MultiSineFunction(float x, float z, float t)
//    {
//        float y = Mathf.Sin(pi * (x + t));
//        y += Mathf.Sin(2f * pi * (x + 2f * t)) / 2f;
//        y *= 2f / 3f;//将y值域从被拓展到[-1.5,1.5]重新缩窄到->[-1,1]
//        return y;
//    }
//    
//    //f(x,z,t) = (sin(pi(x+t)) + sin(pi(z+t))) / 2
//    //有点布的效果
//    static float Sine2DFunction(float x, float z, float t)
//    {
//        //return (float)Math.Sin(pi * (x + t + z));
//        float y = Mathf.Sin(pi * (x + t));
//        y += Mathf.Sin(pi * (z + t));
//        y *= 0.5f;//使用乘法指令能够使得计算更快
//        return y;
//    }
//    
//    //M = sin(pi(x+z+t/2))     Sz = sin(2pi(z+2t))    Sx = sin(pi(x+t))
//    //f(x,z,t) = 4M + Sx + Sz/2
//    static float MultiSine2DFunction (float x, float z, float t) {
//        float y = 4f * Mathf.Sin(pi * (x + z + t * 0.5f));
//        y += Mathf.Sin(pi * (x + t));
//        y += Mathf.Sin(2f * pi * (z + 2f * t)) * 0.5f;
//        y *= 1f / 5.5f;
//        return y;
//    }
//
//    static float Ripple(float x, float z, float t)
//    {
//        float d = Mathf.Sqrt(x * x + z * z);
//        float y = Mathf.Sin(pi * (4f * d - t));
//        y /= 1f + 10f * d;
//        return y;
//    }
    //使用隐函数进行表示
    static Vector3 SineFunction(float x, float z, float t)
    {
        Vector3 p;
        p.x = x;
        p.y = Mathf.Sin(pi * (x + t));
        p.z = z;
        return p;
    }
    static Vector3 Sine2DFunction (float x, float z, float t) {
        Vector3 p;
        p.x = x;
        p.y = Mathf.Sin(pi * (x + t));
        p.y += Mathf.Sin(pi * (z + t));
        p.y *= 0.5f;
        p.z = z;
        return p;
    }
    static Vector3 MultiSineFunction (float x, float z, float t) {
        Vector3 p;
        p.x = x;
        p.y = Mathf.Sin(pi * (x + t));
        p.y += Mathf.Sin(2f * pi * (x + 2f * t)) / 2f;
        p.y *= 2f / 3f;
        p.z = z;
        return p;
    }
    static Vector3 MultiSine2DFunction (float x, float z, float t) {
        Vector3 p;
        p.x = x;
        p.y = 4f * Mathf.Sin(pi * (x + z + t / 2f));
        p.y += Mathf.Sin(pi * (x + t));
        p.y += Mathf.Sin(2f * pi * (z + 2f * t)) * 0.5f;
        p.y *= 1f / 5.5f;
        p.z = z;
        return p;
    }
    static Vector3 Ripple (float x, float z, float t) {
        Vector3 p;
        float d = Mathf.Sqrt(x * x + z * z);
        p.x = x;
        p.y = Mathf.Sin(pi * (4f * d - t));
        p.y /= 1f + 10f * d;
        p.z = z;
        return p;
    }
    
    static Vector3 Cylinder (float u, float v, float t) {
        Vector3 p;
        float r = 0.8f + Mathf.Sin(pi * (6f * u + 2f * v + t)) * 0.2f;

        p.x = r * Mathf.Sin(pi * u);
        p.y = v;
        p.z = r * Mathf.Cos(pi * u);
        return p;
    }

    static Vector3 Sphere(float u, float v, float t)
    {
        Vector3 p;
        float r = 0.8f + Mathf.Sin(pi * (6f * u + t)) * 0.1f;
        r += Mathf.Sin(pi * (4f * v + t)) * 0.1f;
        
        //		float r = Mathf.Cos(pi * 0.5f * v);//构成一个球
        p.x = r * Mathf.Sin(pi * u);
        p.y = Mathf.Sin(pi * 0.5f * v);//与r形成勾股定理构成圆
        p.z = r * Mathf.Cos(pi * u);
        return p;
    }
    
    //核心思维：把球体拉开形成圆环
    static Vector3 Torus (float u, float v, float t) {
        Vector3 p;
        //撕裂完成基本圆环
        float s = Mathf.Cos(pi * 0.5f * v) + 0.5f;
        p.x = s * Mathf.Sin(pi * u);
        p.y = Mathf.Sin(pi * 0.5f * v);
        p.z = s * Mathf.Cos(pi * u);
        
        return p;
    }

    static Vector3 SpindleTorus(float u, float v, float t)
    {
        Vector3 p;
        //内表面构建，使用圆环的次半径
        float r1 = 1f;
        float r2 = 0.5f;
        float s = r2 * Mathf.Cos(pi * v) + r1;
        
        p.x = s * Mathf.Sin(pi * u);
        p.y = r2 * Mathf.Sin(pi * v);
        p.z = s * Mathf.Cos(pi * u);

        return p;
    }
    
    static Vector3 InterestTorus(float u, float v, float t)
    {
        Vector3 p;
        //内表面构建
        float r1 = 0.65f + Mathf.Sin(pi * (6f * u + t)) * 0.1f;
        float r2 = 0.2f + Mathf.Sin(pi * (4f * v + t)) * 0.05f;
        float s = r2 * Mathf.Cos(pi * v) + r1;
        
        p.x = s * Mathf.Sin(pi * u);
        p.y = r2 * Mathf.Sin(pi * v);
        p.z = s * Mathf.Cos(pi * u);

        return p;
    }
    
}
