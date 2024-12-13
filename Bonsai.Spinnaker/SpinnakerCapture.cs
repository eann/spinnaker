using OpenCV.Net;
using SpinnakerNET;
using SpinnakerNET.GenApi;
using System;
using System.ComponentModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Runtime.Remoting.Channels;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Bonsai.Spinnaker
{
    [XmlType(Namespace = Constants.XmlNamespace)]
    [Description("Acquires a sequence of images from a Spinnaker camera.")]
    public class SpinnakerCapture : Source<SpinnakerDataFrame>
    {
        static readonly object systemLock = new object();

        [Description("The optional index of the camera from which to acquire images.")]
        public int? Index { get; set; }

        [Description("The frame rate at which to acquire images.")]
        public double FrameRate { get; set; } = 50.0;

        [TypeConverter(typeof(SerialNumberConverter))]
        [Description("The optional serial number of the camera from which to acquire images.")]
        public string SerialNumber { get; set; }

        [Description("The method used to process bayer color images.")]
        public ColorProcessingAlgorithm ColorProcessing { get; set; } = ColorProcessingAlgorithm.HQ_LINEAR;

        [Description("Camera's pixel format.")]
        public PixelFormatEnums PixFormat { get; set; } = PixelFormatEnums.BayerRG12p;

        protected virtual void Configure(IManagedCamera camera)
        {
            var nodeMap = camera.GetNodeMap();
            var chunkMode = nodeMap.GetNode<IBool>("ChunkModeActive");
            if (chunkMode != null && chunkMode.IsWritable)
            {
                chunkMode.Value = true;
                var chunkSelector = nodeMap.GetNode<IEnum>("ChunkSelector");
                if (chunkSelector != null && chunkSelector.IsReadable)
                {
                    var entries = chunkSelector.Entries;
                    for (int i = 0; i < entries.Length; i++)
                    {
                        var chunkSelectorEntry = entries[i];
                        if (!chunkSelectorEntry.IsAvailable || !chunkSelectorEntry.IsReadable) continue;

                        chunkSelector.Value = chunkSelectorEntry.Value;
                        var chunkEnable = nodeMap.GetNode<IBool>("ChunkEnable");
                        if (chunkEnable == null || chunkEnable.Value || !chunkEnable.IsWritable) continue;
                        chunkEnable.Value = true;
                    }
                }
            }

            var acquisitionMode = nodeMap.GetNode<IEnum>("AcquisitionMode");
            if (acquisitionMode == null || !acquisitionMode.IsWritable)
            {
                throw new InvalidOperationException("Unable to set acquisition mode to continuous.");
            }

            var continuousAcquisitionMode = acquisitionMode.GetEntryByName("Continuous");
            if (continuousAcquisitionMode == null || !continuousAcquisitionMode.IsReadable)
            {
                throw new InvalidOperationException("Unable to set acquisition mode to continuous.");
            }

            acquisitionMode.Value = continuousAcquisitionMode.Symbolic;

            var pixelFormat = nodeMap.GetNode<IEnum>("PixelFormat");
            if (pixelFormat == null || !pixelFormat.IsWritable)
            {
                throw new InvalidOperationException("Unable to set pixel format");
            }

            var selectedPixelFormat = pixelFormat.GetEntryByName(PixFormat.ToString());
            if (selectedPixelFormat == null || !selectedPixelFormat.IsReadable)
            {
                throw new InvalidOperationException("Pixel format not supported by camera.");
            }

            pixelFormat.Value = selectedPixelFormat.Symbolic;


            var exposureMode = nodeMap.GetNode<IEnum>("ExposureMode");
            if (exposureMode == null || !exposureMode.IsWritable)
            {
                throw new InvalidOperationException("Unable to set camera exposure mode.");
            }

            var timedExposureMode = exposureMode.GetEntryByName("Timed");
            if (timedExposureMode == null || !timedExposureMode.IsReadable)
            {
                throw new InvalidOperationException("Unable to set camera exposure mode.");
            }

            exposureMode.Value = timedExposureMode.Symbolic;

            var autoExposure = nodeMap.GetNode<IEnum>("ExposureAuto");
            if (autoExposure == null || !autoExposure.IsWritable)
            {
                throw new InvalidOperationException("Unable to set camera auto exposure.");
            }

            var cameraFrameRateAuto = nodeMap.GetNode<IEnum>("AcquisitionFrameRateAuto");
            if (cameraFrameRateAuto == null || !cameraFrameRateAuto.IsWritable)
            {
                throw new InvalidOperationException("Unable to turn off automatic frame rate.");
            }

            var frameRateAutoOff = cameraFrameRateAuto.GetEntryByName("Off");
            if (frameRateAutoOff == null || !frameRateAutoOff.IsReadable)
            {
                throw new InvalidOperationException("Unable to get automatic frame rate options.");
            }

            cameraFrameRateAuto.Value = frameRateAutoOff.Symbolic;

            var cameraFrameRateEnabled = nodeMap.GetNode<IBool>("AcquisitionFrameRateEnabled");
            if (cameraFrameRateEnabled == null || !cameraFrameRateEnabled.IsWritable)
            {
                throw new InvalidOperationException("Unable to enable camera acquisition frame rate.");
            }

            cameraFrameRateEnabled.Value = true;

            var acquisitionFrameRate = nodeMap.GetNode<IFloat>("AcquisitionFrameRate");
            if (acquisitionFrameRate == null || !acquisitionFrameRate.IsWritable)
            {
                throw new InvalidOperationException("Unable to set camera frame rate.");
            }

            if (FrameRate < acquisitionFrameRate.Min)
            {
                acquisitionFrameRate.Value = acquisitionFrameRate.Min;
            }
            else if (FrameRate > acquisitionFrameRate.Max)
            {
                acquisitionFrameRate.Value = acquisitionFrameRate.Max;
            }
            else
            {
                acquisitionFrameRate.Value = FrameRate;
            }

            var autoExposureMode = autoExposure.GetEntryByName("Continuous");
            if (autoExposureMode == null || !autoExposureMode.IsReadable)
            {
                throw new InvalidOperationException("Unable to set camera auto exposure.");
            }

            autoExposure.Value = autoExposureMode.Symbolic;

            // var exposureTime = nodeMap.GetNode<IFloat>("ExposureTime");
            // if (exposureTime == null || !exposureTime.IsWritable)
            // {
            //     throw new InvalidOperationException("Unable to set camera exposure time.");
            // }

            // exposureTime.Value = exposureTime.Max;
        }

        public override IObservable<SpinnakerDataFrame> Generate()
        {
            return Generate(Observable.Return(Unit.Default));
        }

        protected void DisableSync(IManagedCamera camera)
        {
            var nodeMap = camera.GetNodeMap();
            var lineSelector = nodeMap.GetNode<IEnum>("LineSelector");
            if (lineSelector == null || !lineSelector.IsWritable)
            {
                throw new InvalidOperationException("Unable to get sync output line selector.");
            }
            var line = lineSelector.GetEntryByName("Line1");
            if (line == null || !line.IsReadable)
            {
                throw new InvalidOperationException("Unable to select sync output line.");
            }

            lineSelector.Value = line.Symbolic;

            var lineSource = nodeMap.GetNode<IEnum>("LineSource");
            if (lineSource == null || !lineSource.IsWritable)
            {
                throw new InvalidOperationException("Unable to get sync output line source selector.");
            }
            var userOutput = lineSource.GetEntryByName("UserOutput1");
            if (userOutput == null || !userOutput.IsReadable)
            {
                throw new InvalidOperationException("Unable to get User settable output for sync line.");
            }

            lineSource.Value = userOutput.Symbolic;

            var outputSelector = nodeMap.GetNode<IEnum>("UserOutputSelector");
            if (outputSelector == null || !outputSelector.IsWritable)
            {
                throw new InvalidOperationException("Unable to get User Output selector.");
            }
            var output = outputSelector.GetEntryByName("UserOutputValue1");
            if (output == null || !output.IsReadable)
            {
                throw new InvalidOperationException("Unable to set user output.");
            }

            outputSelector.Value = output.Symbolic;

            var outputValue = nodeMap.GetNode<IBool>("UserOutputValue");
            if (outputValue == null || !outputValue.IsWritable)
            {
                throw new InvalidOperationException("Unable to set sync line.");
            }

            outputValue.Value = true;
        }

        protected void EnableSync(IManagedCamera camera)
        {
            var nodeMap = camera.GetNodeMap();
            var lineSelector = nodeMap.GetNode<IEnum>("LineSelector");
            if (lineSelector == null || !lineSelector.IsWritable)
            {
                throw new InvalidOperationException("Unable to get sync output line selector.");
            }
            var line = lineSelector.GetEntryByName("Line1");
            if (line == null || !line.IsReadable)
            {
                throw new InvalidOperationException("Unable to select sync output line.");
            }

            lineSelector.Value = line.Symbolic;

            var lineSource = nodeMap.GetNode<IEnum>("LineSource");
            if (lineSource == null || !lineSource.IsWritable)
            {
                throw new InvalidOperationException("Unable to get sync output line source selector.");
            }
            var userOutput = lineSource.GetEntryByName("ExposureActive");
            if (userOutput == null || !userOutput.IsReadable)
            {
                throw new InvalidOperationException("Unable to get User settable output for sync line.");
            }

            lineSource.Value = userOutput.Symbolic;
        }

        public IObservable<SpinnakerDataFrame> Generate<TSource>(IObservable<TSource> start)
        {
            return Observable.Create<SpinnakerDataFrame>((observer, cancellationToken) =>
            {
                return Task.Factory.StartNew(async () =>
                {
                    IManagedCamera camera;
                    lock (systemLock)
                    {
                        try
                        {
                            using (var system = new ManagedSystem())
                            {
                                var serialNumber = SerialNumber;
                                var cameraList = system.GetCameras();
                                if (!string.IsNullOrEmpty(serialNumber))
                                {
                                    camera = cameraList.GetBySerial(serialNumber);
                                    if (camera == null)
                                    {
                                        var message = string.Format("Spinnaker camera with serial number {0} was not found.", serialNumber);
                                        throw new InvalidOperationException(message);
                                    }
                                }
                                else
                                {
                                    var index = Index.GetValueOrDefault(0);
                                    if (index < 0 || index >= cameraList.Count)
                                    {
                                        var message = string.Format("No Spinnaker camera was found at index {0}.", index);
                                        throw new InvalidOperationException(message);
                                    }

                                    camera = cameraList.GetByIndex((uint)index);
                                }

                                cameraList.Clear();
                            }
                        }
                        catch (Exception ex)
                        {
                            observer.OnError(ex);
                            throw;
                        }
                    }

                    try
                    {
                        camera.Init();
                        Configure(camera);
                        camera.BeginAcquisition();
                        EnableSync(camera);
                        await start;

                        var imageFormat = PixelFormatEnums.UNKNOWN_PIXELFORMAT;
                        var depth = IplDepth.U8;
                        int channels = 1;
                        IManagedImageProcessor converter = new ManagedImageProcessor();
                        converter.SetColorProcessing(ColorProcessing);
                        using (var cancellation = cancellationToken.Register(camera.EndAcquisition))
                        {
                            while (!cancellationToken.IsCancellationRequested)
                            {
                                using (var image = camera.GetNextImage())
                                {
                                    if (image.IsIncomplete)
                                    {
                                        // drop incomplete frames
                                        continue;
                                    }

                                    if (imageFormat == PixelFormatEnums.UNKNOWN_PIXELFORMAT)
                                    {
                                        if (image.PixelFormat == PixelFormatEnums.Mono8)
                                        {
                                            imageFormat = PixelFormatEnums.Mono8;
                                            depth = IplDepth.U8;
                                            channels = 1;
                                        }
                                        else if (image.PixelFormat.ToString().Contains("Mono"))
                                        {
                                            imageFormat = PixelFormatEnums.Mono16;
                                            depth = IplDepth.U16;
                                            channels = 1;
                                        }
                                        else
                                        {
                                            imageFormat = PixelFormatEnums.BGR8;
                                            depth = IplDepth.U8;
                                            channels = 3;
                                        }
                                    }

                                    using (IManagedImage convertedImage = converter.Convert(image, imageFormat))
                                    {
                                        var width = (int)image.Width;
                                        var height = (int)image.Height;
                                        var output = new IplImage(new Size(width, height), depth, channels);
                                        var tmp = new IplImage(new Size(width, height), depth, channels, convertedImage.DataPtr);
                                        CV.Copy(tmp, output);
                                        observer.OnNext(new SpinnakerDataFrame(output, image.ChunkData));
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex) { observer.OnError(ex); throw; }
                    finally
                    {
                        DisableSync(camera);
                        camera.EndAcquisition();
                        camera.DeInit();
                        camera.Dispose();
                    }
                },
                cancellationToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            });
        }
    }
}
