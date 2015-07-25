﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Drawing;

namespace Ohana3DS_Rebirth.Ohana.PICA200
{
    class PICACommandReader
    {
        private class param
        {
            public uint value;
            public uint register;

            public param(uint _value, uint _register)
            {
                value = _value;
                register = _register;
            }

            public param()
            {
            }
        }
        private List<param>[] commands = new List<param>[0x10000];
        uint currentRegister;

        /// <summary>
        ///     Creates a new PICA200 Command Buffer reader and reads the content.
        /// </summary>
        /// <param name="input">The Input stream where the buffer is located</param>
        /// <param name="wordCount">Total number of words (4 bytes per word) that the buffer have</param>
        /// <param name="ignoreAlign">(Optional) Set to true to ignore potential 0x0 padding words</param>
        public PICACommandReader(Stream input, uint wordCount, bool ignoreAlign = false)
        {
            BinaryReader reader = new BinaryReader(input);
            for (int i = 0; i < commands.Length; i++) commands[i] = new List<param>();

            uint readedWords = 0;
            while (readedWords < wordCount)
            {
                uint parameter = reader.ReadUInt32();
                uint header = reader.ReadUInt32();
                readedWords += 2;

                ushort id = (ushort)(header & 0xffff);
                uint mask = (header >> 16) & 0xf;
                uint extraParameters = (header >> 20) & 0x7ff;
                bool consecutiveWriting = (header & 0x80000000) > 0;

                if (id == PICACommand.blockEnd) break;
                else if (id == PICACommand.vertexShaderFloatUniformConfig) currentRegister = parameter & 0xff;
                commands[id].Add(new param((getParameter(id) & (~mask & 0xf)) | (parameter & (0xfffffff0 | mask)), currentRegister));
                for (int i = 0; i < extraParameters; i++)
                {
                    if (consecutiveWriting) id++;
                    commands[id].Add(new param((getParameter(id) & (~mask & 0xf)) | (reader.ReadUInt32() & (0xfffffff0 | mask)), currentRegister));
                    readedWords++;
                }
                if (!ignoreAlign) while ((input.Position & 7) != 0) reader.ReadUInt32(); //Ignore 0x0 padding Words
            }
        }

        /// <summary>
        ///     Gets the lastest written parameter of a given Command in the buffer.
        /// </summary>
        /// <param name="commandId">ID code of the command</param>
        /// <returns></returns>
        public uint getParameter(ushort commandId)
        {
            if (commands[commandId].Count > 0)
                return commands[commandId][commands[commandId].Count - 1].value;
            else
                return 0;
        }

        /// <summary>
        ///     Gets the parameter written to a given register.
        /// </summary>
        /// <param name="commandId">ID code of the command</param>
        /// <param name="register">Register index number</param>
        /// <returns></returns>
        public uint getParameter(ushort commandId, uint register)
        {
            foreach (param p in commands[commandId]) if (p.register == register) return p.value;
            return 0;
        }

        /// <summary>
        ///     Converts a IEEE 754 encoded float on uint to float.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private float toFloat(uint value)
        {
            byte[] buffer = new byte[4];
            buffer[0] = (byte)(value & 0xff);
            buffer[1] = (byte)((value >> 8) & 0xff);
            buffer[2] = (byte)((value >> 16) & 0xff);
            buffer[3] = (byte)((value >> 24) & 0xff);
            return BitConverter.ToSingle(buffer, 0);
        }

        /// <summary>
        ///     Gets the Total Attributes minus 1.
        /// </summary>
        /// <returns></returns>
        public uint getVSHTotalAttributes()
        {
            return getParameter(PICACommand.vertexShaderTotalAttributes);
        }

        /// <summary>
        ///     Gets an array containing all Vertex Atrributes permutation order.
        /// </summary>
        /// <returns></returns>
        public PICACommand.vshAttribute[] getVSHAttributesBufferPermutation()
        {
            ulong permutation = getParameter(PICACommand.vertexShaderAttributesPermutationLow);
            permutation |= getParameter(PICACommand.vertexShaderAttributesPermutationHigh) << 32;

            PICACommand.vshAttribute[] attributes = new PICACommand.vshAttribute[23];
            for (int attribute = 0; attribute < attributes.Length; attribute++)
            {
                attributes[attribute] = (PICACommand.vshAttribute)((permutation >> (attribute * 4)) & 0xf);
            }
            return attributes;
        }


        /// <summary>
        ///     Gets the Address where the Attributes Buffer is located.
        ///     Note that it may be a Relative address, and may need to be relocated.
        /// </summary>
        /// <returns></returns>
        public uint getVSHAttributesBufferAddress()
        {
            return getParameter(PICACommand.vertexShaderAttributesBufferAddress);
        }

        /// <summary>
        ///     Gets an array containing all formats of the main Attributes Buffer.
        ///     It have the length of each attribute, and the format (byte, float, short...).
        /// </summary>
        /// <returns></returns>
        public PICACommand.attributeFormat[] getVSHAttributesBufferFormat()
        {
            ulong format = getParameter(PICACommand.vertexShaderAttributesBufferFormatLow);
            format |= getParameter(PICACommand.vertexShaderAttributesBufferFormatHigh) << 32;

            PICACommand.attributeFormat[] formats = new PICACommand.attributeFormat[23];
            for (int attribute = 0; attribute < formats.Length; attribute++)
            {
                byte value = (byte)((format >> (attribute * 4)) & 0xf);
                formats[attribute].type = (PICACommand.attributeFormatType)(value & 3);
                formats[attribute].attributeLength = (uint)(value >> 2);
            }
            return formats;
        }

        /// <summary>
        ///     Gets the Address of a given Attributes Buffer.
        ///     It is relative to the Main Buffer address.
        /// </summary>
        /// <param name="bufferIndex">Index number of the buffer (0-11)</param>
        /// <returns></returns>
        public uint getVSHAttributesBufferAddress(byte bufferIndex)
        {
            return getParameter((ushort)(PICACommand.vertexShaderAttributesBuffer0Address + (bufferIndex * 3)));
        }

        /// <summary>
        ///     Gets the Total Attributes of a given Attributes Buffer.
        /// </summary>
        /// <param name="bufferIndex">Index number of the buffer (0-11)</param>
        /// <returns></returns>
        public uint getVSHTotalAttributes(byte bufferIndex)
        {
            uint value = getParameter((ushort)(PICACommand.vertexShaderAttributesBuffer0Stride + (bufferIndex * 3)));
            return value >> 28;
        }

        /// <summary>
        ///     Gets Uniform Booleans used on Vertex Shader.
        /// </summary>
        /// <returns></returns>
        public bool[] getVSHBooleanUniforms()
        {
            bool[] output = new bool[16];

            uint value = getParameter((ushort)PICACommand.vertexShaderBooleanUniforms);
            for (int i = 0; i < 16; i++) output[i] = (value & (1 << i)) > 0;
            return output;
        }

        /// <summary>
        ///     Gets the Permutation of a given Attributes Buffer.
        ///     Values corresponds to a value on the main Permutation.
        /// </summary>
        /// <param name="bufferIndex">Index number of the buffer (0-11)</param>
        /// <returns></returns>
        public uint[] getVSHAttributesBufferPermutation(byte bufferIndex)
        {
            ulong permutation = getParameter((ushort)(PICACommand.vertexShaderAttributesBuffer0Permutation + (bufferIndex * 3)));
            permutation |= (getParameter((ushort)(PICACommand.vertexShaderAttributesBuffer0Stride + (bufferIndex * 3))) & 0xffff) << 32;

            uint[] attributes = new uint[23];
            for (int attribute = 0; attribute < attributes.Length; attribute++)
            {
                attributes[attribute] = (uint)((permutation >> (attribute * 4)) & 0xf);
            }
            return attributes;
        }

        /// <summary>
        ///     Gets the Stride of a given Attributes Buffer.
        /// </summary>
        /// <param name="bufferIndex">Index number of the buffer (0-11)</param>
        /// <returns></returns>
        public byte getVSHAttributesBufferStride(byte bufferIndex)
        {
            uint value = getParameter((ushort)(PICACommand.vertexShaderAttributesBuffer0Stride + (bufferIndex * 3)));
            return (byte)((value >> 16) & 0xff);
        }

        /// <summary>
        ///     Gets the Float Uniform data array from the given register.
        /// </summary>
        /// <param name="register">Index number of the register (observed values: 6 and 7)</param>
        /// <returns></returns>
        public Stack<float> getVSHFloatUniformData(uint register)
        {
            Stack<float> data = new Stack<float>();
            for (int id = 0; id < 8; id++)
            {
                foreach (param p in commands[(ushort)(PICACommand.vertexShaderFloatUniformData + id)])
                {
                    if (p.register == register) data.Push(toFloat(p.value));
                }
            }
            return data;
        }

        /// <summary>
        ///     Gets the Address where the Index Buffer is located.
        /// </summary>
        /// <returns></returns>
        public uint getIndexBufferAddress()
        {
            return getParameter(PICACommand.indexBufferConfig) & 0x7fffffff;
        }

        /// <summary>
        ///     Gets the Format of the Index Buffer (byte or short).
        /// </summary>
        /// <returns></returns>
        public PICACommand.indexBufferFormat getIndexBufferFormat()
        {
            return (PICACommand.indexBufferFormat)(getParameter(PICACommand.indexBufferConfig) >> 31);
        }

        /// <summary>
        ///     Gets the total number of vertices indexed by the Index Buffer.
        /// </summary>
        /// <returns></returns>
        public uint getIndexBufferTotalVertices()
        {
            return getParameter(PICACommand.indexBufferTotalVertices);
        }

        /// <summary>
        ///     Gets TEV Stage parameters.
        /// </summary>
        /// <param name="stage">The stage (0-5)</param>
        /// <returns></returns>
        public RenderBase.OTextureCombiner getTevStage(byte stage)
        {
            RenderBase.OTextureCombiner output = new RenderBase.OTextureCombiner();

            ushort baseCommand = 0;
            switch (stage)
            {
                case 0: baseCommand = PICACommand.tevStage0Config0; break;
                case 1: baseCommand = PICACommand.tevStage1Config0; break;
                case 2: baseCommand = PICACommand.tevStage2Config0; break;
                case 3: baseCommand = PICACommand.tevStage3Config0; break;
                case 4: baseCommand = PICACommand.tevStage4Config0; break;
                case 5: baseCommand = PICACommand.tevStage5Config0; break;
                default: throw new Exception("PICACommandReader: Invalid TevStage number!");
            }

            //Source
            uint source = getParameter(baseCommand);

            output.rgbSource[0] = (RenderBase.OCombineSource)(source & 0xf);
            output.rgbSource[1] = (RenderBase.OCombineSource)((source >> 4) & 0xf);
            output.rgbSource[2] = (RenderBase.OCombineSource)((source >> 8) & 0xf);

            output.alphaSource[0] = (RenderBase.OCombineSource)((source >> 16) & 0xf);
            output.alphaSource[1] = (RenderBase.OCombineSource)((source >> 20) & 0xf);
            output.alphaSource[2] = (RenderBase.OCombineSource)((source >> 24) & 0xf);

            //Operand
            uint operand = getParameter((ushort)(baseCommand + 1));

            output.rgbOperand[0] = (RenderBase.OCombineOperandRgb)(operand & 0xf);
            output.rgbOperand[1] = (RenderBase.OCombineOperandRgb)((operand >> 4) & 0xf);
            output.rgbOperand[2] = (RenderBase.OCombineOperandRgb)((operand >> 8) & 0xf);

            output.alphaOperand[0] = (RenderBase.OCombineOperandAlpha)((operand >> 12) & 0xf);
            output.alphaOperand[1] = (RenderBase.OCombineOperandAlpha)((operand >> 16) & 0xf);
            output.alphaOperand[2] = (RenderBase.OCombineOperandAlpha)((operand >> 20) & 0xf);

            //Operator
            uint combine = getParameter((ushort)(baseCommand + 2));

            output.combineRgb = (RenderBase.OCombineOperator)(combine & 0xffff);
            output.combineAlpha = (RenderBase.OCombineOperator)(combine >> 16);

            //Scale
            uint scale = getParameter((ushort)(baseCommand + 4));

            output.rgbScale = (ushort)((scale & 0xffff) + 1);
            output.alphaScale = (ushort)((scale >> 16) + 1);

            return output;
        }

        /// <summary>
        ///     Gets the Fragment Buffer Color.
        /// </summary>
        /// <returns></returns>
        public Color getFragmentBufferColor()
        {
            uint rgba = getParameter(PICACommand.fragmentBufferColor);
            return Color.FromArgb((byte)(rgba >> 24),
                (byte)(rgba & 0xff),
                (byte)((rgba >> 8) & 0xff),
                (byte)((rgba >> 16) & 0xff));
        }

        /// <summary>
        ///     Gets Blending operation parameters.
        /// </summary>
        /// <returns></returns>
        public RenderBase.OBlendOperation getBlendOperation()
        {
            RenderBase.OBlendOperation output = new RenderBase.OBlendOperation();

            uint value = getParameter(PICACommand.blendConfig);
            output.rgbFunctionSource = (RenderBase.OBlendFunction)((value >> 16) & 0xf);
            output.rgbFunctionDestination = (RenderBase.OBlendFunction)((value >> 20) & 0xf);
            output.alphaFunctionSource = (RenderBase.OBlendFunction)((value >> 24) & 0xf);
            output.alphaFunctionDestination = (RenderBase.OBlendFunction)((value >> 28) & 0xf);
            output.rgbBlendEquation = (RenderBase.OBlendEquation)(value & 0xff);
            output.alphaBlendEquation = (RenderBase.OBlendEquation)((value >> 8) & 0xff);

            return output;
        }

        /// <summary>
        ///     Gets the Logical operation applied to Fragment colors.
        /// </summary>
        /// <returns></returns>
        public RenderBase.OLogicalOperation getColorLogicOperation()
        {
            return (RenderBase.OLogicalOperation)(getParameter(PICACommand.colorLogicOperationConfig) & 0xf);
        }

        /// <summary>
        ///     Gets the parameters used for Alpha testing.
        /// </summary>
        /// <returns></returns>
        public RenderBase.OAlphaTest getAlphaTest()
        {
            RenderBase.OAlphaTest output = new RenderBase.OAlphaTest();

            uint value = getParameter(PICACommand.alphaTestConfig);
            output.isTestEnabled = (value & 1) > 0;
            output.testFunction = (RenderBase.OTestFunction)((value >> 4) & 0xf);
            output.testReference = ((value >> 8) & 0xff);

            return output;
        }

        /// <summary>
        ///     Gets the parameters used for Stencil testing.
        /// </summary>
        /// <returns></returns>
        public RenderBase.OStencilOperation getStencilTest()
        {
            RenderBase.OStencilOperation output = new RenderBase.OStencilOperation();

            //Test
            uint test = getParameter(PICACommand.stencilTestConfig);

            output.isTestEnabled = (test & 1) > 0;
            output.testFunction = (RenderBase.OTestFunction)((test >> 4) & 0xf);
            output.testReference = (test >> 16) & 0xff;
            output.testMask = (test >> 24);

            //Operation
            uint operation = getParameter(PICACommand.stencilOperationConfig);

            output.failOperation = (RenderBase.OStencilOp)(operation & 0xf);
            output.zFailOperation = (RenderBase.OStencilOp)((operation >> 4) & 0xf);
            output.passOperation = (RenderBase.OStencilOp)((operation >> 8) & 0xf);

            return output;
        }

        /// <summary>
        ///     Gets the parameters used for Depth testing.
        /// </summary>
        /// <returns></returns>
        public RenderBase.ODepthOperation getDepthTest()
        {
            RenderBase.ODepthOperation output = new RenderBase.ODepthOperation();

            uint value = getParameter(PICACommand.depthTestConfig);
            output.isTestEnabled = (value & 1) > 0;
            output.testFunction = (RenderBase.OTestFunction)((value >> 4) & 0xf);
            output.isMaskEnabled = (value & 0x1000) > 0;

            return output;
        }

        /// <summary>
        ///     Gets the Culling mode.
        /// </summary>
        /// <returns></returns>
        public RenderBase.OCullMode getCullMode()
        {
            uint value = getParameter(PICACommand.cullModeConfig);
            return (RenderBase.OCullMode)(value & 0xf);
        }

        /// <summary>
        ///     Gets the 1D LookUp table sampler used on the Fragment Shader lighting.
        /// </summary>
        /// <returns></returns>
        public float[] getFSHLookUpTable()
        {
            float[] output = new float[256];

            for (int i = 0; i < 256; i++)
            {
                output[i] = toFloat(commands[PICACommand.fragmentShaderLookUpTableData][i].value);
            }
            return output;
        }

        /// <summary>
        ///     Gets the Input used to pick a value from the LookUp Table on Fragment Shader.
        /// </summary>
        /// <returns></returns>
        public PICACommand.fragmentSamplerInput getReflectanceSamplerInput()
        {
            PICACommand.fragmentSamplerInput output = new PICACommand.fragmentSamplerInput();

            uint value = getParameter(PICACommand.reflectanceSamplerInput);
            output.r = (RenderBase.OFragmentSamplerInput)((value >> 24) & 0xf);
            output.g = (RenderBase.OFragmentSamplerInput)((value >> 20) & 0xf);
            output.b = (RenderBase.OFragmentSamplerInput)((value >> 16) & 0xf);
            output.d0 = (RenderBase.OFragmentSamplerInput)(value & 0xf);
            output.d1 = (RenderBase.OFragmentSamplerInput)((value >> 4) & 0xf);
            output.fresnel = (RenderBase.OFragmentSamplerInput)((value >> 12) & 0xf);

            return output;
        }

        /// <summary>
        ///     Gets the scale used on the value on Fragment Shader.
        /// </summary>
        /// <returns></returns>
        public PICACommand.fragmentSamplerScale getReflectanceSamplerScale()
        {
            PICACommand.fragmentSamplerScale output = new PICACommand.fragmentSamplerScale();

            uint value = getParameter(PICACommand.reflectanceSamplerScale);
            output.r = (RenderBase.OFragmentSamplerScale)((value >> 24) & 0xf);
            output.g = (RenderBase.OFragmentSamplerScale)((value >> 20) & 0xf);
            output.b = (RenderBase.OFragmentSamplerScale)((value >> 16) & 0xf);
            output.d0 = (RenderBase.OFragmentSamplerScale)(value & 0xf);
            output.d1 = (RenderBase.OFragmentSamplerScale)((value >> 4) & 0xf);
            output.fresnel = (RenderBase.OFragmentSamplerScale)((value >> 12) & 0xf);

            return output;
        }

        /// <summary>
        ///     Gets the Address of the texture at Texture Unit 0.
        /// </summary>
        /// <returns></returns>
        public uint getTexUnit0Address()
        {
            return getParameter(PICACommand.texUnit0Address);
        }

        /// <summary>
        ///     Gets the mapping parameters used on Texture Unit 0,
        ///     such as the wrapping mode, filtering and so on.
        /// </summary>
        /// <returns></returns>
        public RenderBase.OTextureMapper getTexUnit0Mapper()
        {
            RenderBase.OTextureMapper output = new RenderBase.OTextureMapper();

            uint value = getParameter(PICACommand.texUnit0Param);
            output.magFilter = (RenderBase.OTextureMagFilter)((value >> 1) & 1);
            output.minFilter = (RenderBase.OTextureMinFilter)(((value >> 2) & 1) | ((value >> 23) & 2));
            output.wrapU = (RenderBase.OTextureWrap)((value >> 8) & 3);
            output.wrapV = (RenderBase.OTextureWrap)((value >> 12) & 3);

            return output;
        }

        /// <summary>
        ///     Gets the border color used on Texture Unit 0,
        ///     when the wrapping mode is set to Border.
        /// </summary>
        /// <returns></returns>
        public Color getTexUnit0BorderColor()
        {
            uint rgba = getParameter(PICACommand.texUnit0BorderColor);
            return Color.FromArgb((byte)(rgba >> 24),
                (byte)(rgba & 0xff),
                (byte)((rgba >> 8) & 0xff),
                (byte)((rgba >> 16) & 0xff));
        }

        /// <summary>
        ///     Gets the resolution of the texture at Texture Unit 0.
        /// </summary>
        /// <returns></returns>
        public Size getTexUnit0Size()
        {
            uint value = getParameter(PICACommand.texUnit0Size);
            return new Size((int)(value >> 16), (int)(value & 0xffff));
        }

        /// <summary>
        ///     Gets the encoded format of the texture at Texture Unit 0.
        /// </summary>
        public TextureCodec.OTextureFormat getTexUnit0Format()
        {
            return (TextureCodec.OTextureFormat)getParameter(PICACommand.texUnit0Type);
        }

        /// <summary>
        ///     Gets the Address of the texture at Texture Unit 1.
        /// </summary>
        /// <returns></returns>
        public uint getTexUnit1Address()
        {
            return getParameter(PICACommand.texUnit1Address);
        }

        /// <summary>
        ///     Gets the mapping parameters used on Texture Unit 1,
        ///     such as the wrapping mode, filtering and so on.
        /// </summary>
        /// <returns></returns>
        public RenderBase.OTextureMapper getTexUnit1Mapper()
        {
            RenderBase.OTextureMapper output = new RenderBase.OTextureMapper();

            uint value = getParameter(PICACommand.texUnit1Param);
            output.magFilter = (RenderBase.OTextureMagFilter)((value >> 1) & 1);
            output.minFilter = (RenderBase.OTextureMinFilter)(((value >> 2) & 1) | ((value >> 23) & 2));
            output.wrapU = (RenderBase.OTextureWrap)((value >> 8) & 3);
            output.wrapV = (RenderBase.OTextureWrap)((value >> 12) & 3);

            return output;
        }

        /// <summary>
        ///     Gets the border color used on Texture Unit 1,
        ///     when the wrapping mode is set to Border.
        /// </summary>
        /// <returns></returns>
        public Color getTexUnit1BorderColor()
        {
            uint rgba = getParameter(PICACommand.texUnit1BorderColor);
            return Color.FromArgb((byte)(rgba >> 24),
                (byte)(rgba & 0xff),
                (byte)((rgba >> 8) & 0xff),
                (byte)((rgba >> 16) & 0xff));
        }

        /// <summary>
        ///     Gets the resolution of the texture at Texture Unit 1.
        /// </summary>
        /// <returns></returns>
        public Size getTexUnit1Size()
        {
            uint value = getParameter(PICACommand.texUnit1Size);
            return new Size((int)(value >> 16), (int)(value & 0xffff));
        }

        /// <summary>
        ///     Gets the encoded format of the texture at Texture Unit 1.
        /// </summary>
        public TextureCodec.OTextureFormat getTexUnit1Format()
        {
            return (TextureCodec.OTextureFormat)getParameter(PICACommand.texUnit1Type);
        }

        /// <summary>
        ///     Gets the Address of the texture at Texture Unit 2.
        /// </summary>
        /// <returns></returns>
        public uint getTexUnit2Address()
        {
            return getParameter(PICACommand.texUnit2Address);
        }

        /// <summary>
        ///     Gets the mapping parameters used on Texture Unit 2,
        ///     such as the wrapping mode, filtering and so on.
        /// </summary>
        /// <returns></returns>
        public RenderBase.OTextureMapper getTexUnit2Mapper()
        {
            RenderBase.OTextureMapper output = new RenderBase.OTextureMapper();

            uint value = getParameter(PICACommand.texUnit2Param);
            output.magFilter = (RenderBase.OTextureMagFilter)((value >> 1) & 1);
            output.minFilter = (RenderBase.OTextureMinFilter)(((value >> 2) & 1) | ((value >> 23) & 2));
            output.wrapU = (RenderBase.OTextureWrap)((value >> 8) & 3);
            output.wrapV = (RenderBase.OTextureWrap)((value >> 12) & 3);

            return output;
        }

        /// <summary>
        ///     Gets the border color used on Texture Unit 2,
        ///     when the wrapping mode is set to Border.
        /// </summary>
        /// <returns></returns>
        public Color getTexUnit2BorderColor()
        {
            uint rgba = getParameter(PICACommand.texUnit2BorderColor);
            return Color.FromArgb((byte)(rgba >> 24),
                (byte)(rgba & 0xff),
                (byte)((rgba >> 8) & 0xff),
                (byte)((rgba >> 16) & 0xff));
        }

        /// <summary>
        ///     Gets the resolution of the texture at Texture Unit 2.
        /// </summary>
        /// <returns></returns>
        public Size getTexUnit2Size()
        {
            uint value = getParameter(PICACommand.texUnit2Size);
            return new Size((int)(value >> 16), (int)(value & 0xffff));
        }

        /// <summary>
        ///     Gets the encoded format of the texture at Texture Unit 2.
        /// </summary>
        public TextureCodec.OTextureFormat getTexUnit2Format()
        {
            return (TextureCodec.OTextureFormat)getParameter(PICACommand.texUnit2Type);
        }
    }
}
