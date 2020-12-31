using System;
using System.Collections.Generic;
using System.Text;

namespace LingK
{
    // Adapted from: https://stackoverflow.com/questions/32664/is-there-a-constraint-that-restricts-my-generic-method-to-numeric-types/4834066#4834066
    public interface INumericPolicy<T>
    {
        T Zero();
        T Add(T a, T b);
        T Subtract(T a, T b);
        T Increment(ref T a, T b);
        T Multiply(T a, T b);
        T Divide(T a, T b);
        T Parse(string s);
        bool TryParse(string s, out T a);
    }

    public class NumericPolicies :
        INumericPolicy<int>,
        INumericPolicy<float>
    {
        public static NumericPolicies Instance = new NumericPolicies();

        int INumericPolicy<int>.Zero() => 0;
        float INumericPolicy<float>.Zero() => 0f;

        int INumericPolicy<int>.Add(int a, int b) => a + b;
        float INumericPolicy<float>.Add(float a, float b) => a + b;

        int INumericPolicy<int>.Subtract(int a, int b) => a - b;
        float INumericPolicy<float>.Subtract(float a, float b) => a - b;

        int INumericPolicy<int>.Increment(ref int a, int b) => a += b;
        float INumericPolicy<float>.Increment(ref float a, float b) => a += b;

        int INumericPolicy<int>.Multiply(int a, int b) => a * b;
        float INumericPolicy<float>.Multiply(float a, float b) => a * b;

        int INumericPolicy<int>.Divide(int a, int b) => a / b;
        float INumericPolicy<float>.Divide(float a, float b) => a / b;

        int INumericPolicy<int>.Parse(string s) => int.Parse(s);
        float INumericPolicy<float>.Parse(string s) => float.Parse(s);

        bool INumericPolicy<int>.TryParse(string s, out int a) => int.TryParse(s, out a);
        bool INumericPolicy<float>.TryParse(string s, out float a) => float.TryParse(s, out a);
    }
}
