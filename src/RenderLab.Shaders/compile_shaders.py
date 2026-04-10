"""
Generate SPIR-V binaries for RenderLab triangle shaders.
Run: python compile_shaders.py
Produces: triangle.vert.spv, triangle.frag.spv

Uses glslc if available, otherwise generates SPIR-V directly.
"""
import struct, shutil, subprocess, sys, os

SHADER_SOURCES = [
    "triangle.vert", "triangle.frag",
    "fullscreen.vert", "postprocess.frag",
    "gbuffer.vert", "gbuffer.frag",
    "lighting.frag", "tonemap.frag",
    "imgui.vert", "imgui.frag",
    "debugviz.frag",
]

def try_glslc():
    glslc = shutil.which("glslc")
    if not glslc:
        return False
    try:
        script_dir = os.path.dirname(os.path.abspath(__file__))
        for shader in SHADER_SOURCES:
            src = os.path.join(script_dir, shader)
            if not os.path.exists(src):
                continue
            dst = os.path.join(script_dir, shader + ".spv")
            subprocess.run([glslc, src, "-o", dst], check=True)
            print(f"  glslc: {shader} -> {shader}.spv")
        return True
    except Exception as e:
        print(f"  glslc failed: {e}, falling back to manual SPIR-V generation")
        return False

# ── SPIR-V binary builder ────────────────────────────────────────────

MAGIC = 0x07230203
VERSION = 0x00010000  # SPIR-V 1.0

# Opcodes
OpCapability = 17; OpMemoryModel = 14; OpEntryPoint = 15; OpExecutionMode = 16
OpSource = 3; OpDecorate = 71; OpMemberDecorate = 72
OpTypeVoid = 19; OpTypeFunction = 33; OpTypeFloat = 22; OpTypeVector = 23
OpTypeStruct = 30; OpTypePointer = 32; OpTypeInt = 21
OpVariable = 59; OpConstant = 43; OpFunction = 54; OpFunctionEnd = 56
OpLabel = 248; OpLoad = 61; OpStore = 62; OpAccessChain = 65
OpCompositeConstruct = 80; OpCompositeExtract = 81; OpReturn = 253

# Enums
CapShader = 1; MemLogical = 0; MemGLSL450 = 1
ExecVertex = 0; ExecFragment = 4; ExecOriginUpperLeft = 7
SCInput = 1; SCOutput = 3
DecBlock = 2; DecLocation = 30; DecBuiltIn = 11
BuiltInPosition = 0; FuncNone = 0; SrcGLSL = 2

def str_words(s):
    b = s.encode('ascii') + b'\x00'
    while len(b) % 4: b += b'\x00'
    return [struct.unpack('<I', b[i:i+4])[0] for i in range(0, len(b), 4)]

def f32(f): return struct.unpack('<I', struct.pack('<f', f))[0]
def i32(i): return struct.unpack('<I', struct.pack('<i', i))[0]

class SpvBuilder:
    def __init__(self):
        self.words = []
    def inst(self, op, *operands):
        self.words.append(((1 + len(operands)) << 16) | op)
        self.words.extend(operands)
    def entry_point(self, model, func_id, name, *interfaces):
        sw = str_words(name)
        n = 1 + 1 + 1 + len(sw) + len(interfaces)
        self.words.append((n << 16) | OpEntryPoint)
        self.words.extend([model, func_id, *sw, *interfaces])
    def build(self, bound):
        header = [MAGIC, VERSION, 0, bound, 0]
        data = header + self.words
        return struct.pack(f'<{len(data)}I', *data)

# ── Vertex shader ────────────────────────────────────────────────────

def build_vert():
    # IDs
    VOID=1; FN_VOID=2; FLOAT=3; V2F=4; V3F=5; V4F=6
    PV_STRUCT=7; P_OUT_PV=8; GL_POS_VAR=9; INT=10; INT_0=11
    P_OUT_V4=12; P_IN_V2=13; P_IN_V3=14; P_OUT_V3=15
    IN_POS=16; IN_COLOR=17; OUT_COLOR=18; F0=19; F1=20
    MAIN=21; LBL=22; POS=23; COL=24; PX=25; PY=26; GPOS=27; GPTR=28
    BOUND=29

    b = SpvBuilder()
    b.inst(OpCapability, CapShader)
    b.inst(OpMemoryModel, MemLogical, MemGLSL450)
    b.entry_point(ExecVertex, MAIN, "main", IN_POS, IN_COLOR, GL_POS_VAR, OUT_COLOR)
    b.inst(OpSource, SrcGLSL, 450)

    # Decorations
    b.inst(OpMemberDecorate, PV_STRUCT, 0, DecBuiltIn, BuiltInPosition)
    b.inst(OpDecorate, PV_STRUCT, DecBlock)
    b.inst(OpDecorate, IN_POS, DecLocation, 0)
    b.inst(OpDecorate, IN_COLOR, DecLocation, 1)
    b.inst(OpDecorate, OUT_COLOR, DecLocation, 0)

    # Types
    b.inst(OpTypeVoid, VOID)
    b.inst(OpTypeFunction, FN_VOID, VOID)
    b.inst(OpTypeFloat, FLOAT, 32)
    b.inst(OpTypeVector, V2F, FLOAT, 2)
    b.inst(OpTypeVector, V3F, FLOAT, 3)
    b.inst(OpTypeVector, V4F, FLOAT, 4)
    b.inst(OpTypeStruct, PV_STRUCT, V4F)
    b.inst(OpTypePointer, P_OUT_PV, SCOutput, PV_STRUCT)
    b.inst(OpTypeInt, INT, 32, 1)
    b.inst(OpTypePointer, P_OUT_V4, SCOutput, V4F)
    b.inst(OpTypePointer, P_IN_V2, SCInput, V2F)
    b.inst(OpTypePointer, P_IN_V3, SCInput, V3F)
    b.inst(OpTypePointer, P_OUT_V3, SCOutput, V3F)

    # Variables
    b.inst(OpVariable, P_OUT_PV, GL_POS_VAR, SCOutput)
    b.inst(OpVariable, P_IN_V2, IN_POS, SCInput)
    b.inst(OpVariable, P_IN_V3, IN_COLOR, SCInput)
    b.inst(OpVariable, P_OUT_V3, OUT_COLOR, SCOutput)

    # Constants
    b.inst(OpConstant, INT, INT_0, i32(0))
    b.inst(OpConstant, FLOAT, F0, f32(0.0))
    b.inst(OpConstant, FLOAT, F1, f32(1.0))

    # Function
    b.inst(OpFunction, VOID, MAIN, FuncNone, FN_VOID)
    b.inst(OpLabel, LBL)
    b.inst(OpLoad, V2F, POS, IN_POS)
    b.inst(OpLoad, V3F, COL, IN_COLOR)
    b.inst(OpCompositeExtract, FLOAT, PX, POS, 0)
    b.inst(OpCompositeExtract, FLOAT, PY, POS, 1)
    b.inst(OpCompositeConstruct, V4F, GPOS, PX, PY, F0, F1)
    b.inst(OpAccessChain, P_OUT_V4, GPTR, GL_POS_VAR, INT_0)
    b.inst(OpStore, GPTR, GPOS)
    b.inst(OpStore, OUT_COLOR, COL)
    b.inst(OpReturn)
    b.inst(OpFunctionEnd)

    return b.build(BOUND)

# ── Fragment shader ──────────────────────────────────────────────────

def build_frag():
    VOID=1; FN_VOID=2; FLOAT=3; V3F=4; V4F=5
    P_IN_V3=6; P_OUT_V4=7; FRAG_COLOR=8; OUT_COLOR=9; F1=10
    MAIN=11; LBL=12; COL=13; OUT_VEC=14
    BOUND=15

    b = SpvBuilder()
    b.inst(OpCapability, CapShader)
    b.inst(OpMemoryModel, MemLogical, MemGLSL450)
    b.entry_point(ExecFragment, MAIN, "main", FRAG_COLOR, OUT_COLOR)
    b.inst(OpExecutionMode, MAIN, ExecOriginUpperLeft)
    b.inst(OpSource, SrcGLSL, 450)

    b.inst(OpDecorate, FRAG_COLOR, DecLocation, 0)
    b.inst(OpDecorate, OUT_COLOR, DecLocation, 0)

    # Types
    b.inst(OpTypeVoid, VOID)
    b.inst(OpTypeFunction, FN_VOID, VOID)
    b.inst(OpTypeFloat, FLOAT, 32)
    b.inst(OpTypeVector, V3F, FLOAT, 3)
    b.inst(OpTypeVector, V4F, FLOAT, 4)
    b.inst(OpTypePointer, P_IN_V3, SCInput, V3F)
    b.inst(OpTypePointer, P_OUT_V4, SCOutput, V4F)

    # Variables
    b.inst(OpVariable, P_IN_V3, FRAG_COLOR, SCInput)
    b.inst(OpVariable, P_OUT_V4, OUT_COLOR, SCOutput)

    # Constants
    b.inst(OpConstant, FLOAT, F1, f32(1.0))

    # Function
    b.inst(OpFunction, VOID, MAIN, FuncNone, FN_VOID)
    b.inst(OpLabel, LBL)
    b.inst(OpLoad, V3F, COL, FRAG_COLOR)
    b.inst(OpCompositeExtract, FLOAT, BOUND, COL, 0)  # r — use BOUND as temp, reassign

    # Actually, for vec4(fragColor, 1.0) we need to extract components and construct
    # Let me use proper IDs
    # Oops, I ran out of pre-assigned IDs. Let me redo with more.
    pass

    # Redo with correct IDs
    return None

def build_frag_v2():
    VOID=1; FN_VOID=2; FLOAT=3; V3F=4; V4F=5
    P_IN_V3=6; P_OUT_V4=7; FRAG_COLOR=8; OUT_COLOR=9; F1=10
    MAIN=11; LBL=12; COL=13; CR=14; CG=15; CB=16; OUT_VEC=17
    BOUND=18

    b = SpvBuilder()
    b.inst(OpCapability, CapShader)
    b.inst(OpMemoryModel, MemLogical, MemGLSL450)
    b.entry_point(ExecFragment, MAIN, "main", FRAG_COLOR, OUT_COLOR)
    b.inst(OpExecutionMode, MAIN, ExecOriginUpperLeft)
    b.inst(OpSource, SrcGLSL, 450)

    b.inst(OpDecorate, FRAG_COLOR, DecLocation, 0)
    b.inst(OpDecorate, OUT_COLOR, DecLocation, 0)

    b.inst(OpTypeVoid, VOID)
    b.inst(OpTypeFunction, FN_VOID, VOID)
    b.inst(OpTypeFloat, FLOAT, 32)
    b.inst(OpTypeVector, V3F, FLOAT, 3)
    b.inst(OpTypeVector, V4F, FLOAT, 4)
    b.inst(OpTypePointer, P_IN_V3, SCInput, V3F)
    b.inst(OpTypePointer, P_OUT_V4, SCOutput, V4F)

    b.inst(OpVariable, P_IN_V3, FRAG_COLOR, SCInput)
    b.inst(OpVariable, P_OUT_V4, OUT_COLOR, SCOutput)

    b.inst(OpConstant, FLOAT, F1, f32(1.0))

    b.inst(OpFunction, VOID, MAIN, FuncNone, FN_VOID)
    b.inst(OpLabel, LBL)
    b.inst(OpLoad, V3F, COL, FRAG_COLOR)
    b.inst(OpCompositeExtract, FLOAT, CR, COL, 0)
    b.inst(OpCompositeExtract, FLOAT, CG, COL, 1)
    b.inst(OpCompositeExtract, FLOAT, CB, COL, 2)
    b.inst(OpCompositeConstruct, V4F, OUT_VEC, CR, CG, CB, F1)
    b.inst(OpStore, OUT_COLOR, OUT_VEC)
    b.inst(OpReturn)
    b.inst(OpFunctionEnd)

    return b.build(BOUND)


if __name__ == "__main__":
    script_dir = os.path.dirname(os.path.abspath(__file__))

    if try_glslc():
        print("Shaders compiled with glslc.")
        sys.exit(0)

    print("  glslc not found, generating SPIR-V directly...")

    vert_path = os.path.join(script_dir, "triangle.vert.spv")
    frag_path = os.path.join(script_dir, "triangle.frag.spv")

    with open(vert_path, 'wb') as f:
        f.write(build_vert())
    print(f"  -> {vert_path}")

    with open(frag_path, 'wb') as f:
        f.write(build_frag_v2())
    print(f"  -> {frag_path}")

    print("Done.")
