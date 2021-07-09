namespace KinectV2Viewer.Custom
{
    public class SystemValidationReport_custom
    {
        //Microsoft Visual C++ 2017/2019 Redistributable
        public bool MicrosoftVisualCPlusPlusRedistributableExists { get; set; }
        //Nvida CUDA Toolkit 10.2
        public bool CudaExists { get; set; }
        //Nvida cuDNN v7.5.2 for CUDA 10.2
        public bool CudnnExists { get; set; }
        //Graphic device name
    }
}
