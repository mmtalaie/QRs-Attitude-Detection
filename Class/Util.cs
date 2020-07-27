using System;
using AForge.Math;


namespace QRs
{

    enum VisualizationType
    {
        // Hightlight glyph with border only
        BorderOnly,
        // Hightlight glyph with border and put its name in the center
        Name,
        // Substitue glyph with its image
        Image,
        // Show 3D model over the glyph
        Model
    }
    public static class Util
    {
        public static void MyExtractYawPitchRoll(this Matrix3x3 Rmat, out float pitch, out float roll, out float yaw)
        {

            pitch = (float)Math.Asin(-Rmat.V02);
            yaw = (float)Math.Atan2(Rmat.V01, Rmat.V00);
            roll = (float)Math.Atan2(Rmat.V12, Rmat.V22);

            yaw *= (float)(180.0 / Math.PI);
            pitch *= (float)(180.0 / Math.PI);
            roll *= (float)(180.0 / Math.PI);
        }
    }
    public class KalmanFilter
    {
        private double A, H, Q, R, P, x;

        public KalmanFilter(double A, double H, double Q, double R, double initial_P, double initial_x)
        {
            this.A = A;
            this.H = H;
            this.Q = Q;//noise
            this.R = R;//assumed environmet noise
            this.P = initial_P;
            this.x = initial_x;
        }

        public double Output(double input)
        {
            // time update - prediction
            x = A * x;
            P = A * P * A + Q;

            // measurement update - correction
            double K = P * H / (H * P * H + R);
            x += K * (input - H * x);
            P = (1 - K * H) * P;

            return x;
        }
    }
}
