/*
Paged SDF glyph variant of KF_VRChatAvatarTextShader.
Derived from KillFrenzyAvatarText, Copyright 2023 KillFrenzy / Evan Tran (MIT License):

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

DATA LAYOUT (see kat_codec.py)
  _CharN  (N = 0..127)  material float written by the animator = 127 * KAT_CharSync  -> k in -127..127
                        symbol s = k < 0 ? k + 255 : k    = the glyph SLOT (0..254) inside its page
  _PageM                same encoding as _CharN, but what it holds depends on the PAGE FORMAT (keyword KAT_PAGE_FORMAT_3):
    format 2 (default, keyword off)  M = 0..63   a PAIR symbol pa * 15 + pb; slot 2M uses pa (page = pair / 15), slot 2M+1 uses pb
                                     (page = pair % 15), so pages 0..14
    format 3 (keyword on)            M = 0..127  the page of slot M directly, so pages 0..253
  page 0  : narrow glyphs, cells 1:2 (16 x 8 grid). Slot 254 = FOLLOW ("right half of the wide glyph in the previous slot")
  page 1+ : wide glyphs, square cells (16 x 16 grid) shown across two slots [lead][FOLLOW]
  _GlyphArray: Texture2DArray, slice = page, single channel SDF, 0.5 = glyph edge. Import as LINEAR (no sRGB).
  _ColorArray (keyword KAT_COLOR_GLYPHS): optional Texture2DArray holding the emoji pages (RGBA, BC7, sRGB).
                Every page from _ColorPageStart upwards is an emoji page, and its slice is page - _ColorPageStart. They use the
                same 16 x 16 square cells as the wide pages, with [lead][FOLLOW] across two slots. Emoji ignore the text colour,
                outline and weight; only the text colour's alpha reaches them.
  Character/page material properties are packed four scalars per constant register internally; _CharN/_PageN remain the public property names.
  Grow distance, amount, tail geometry and the extent-scan budget are exposed as material properties.
  ExtentScanSlots is a deliberate performance/accuracy tradeoff for shrink-to-fit box layout; glyph rendering still uses all slots.

BOX
  The vertex shader scans the animated slots once per vertex and finds the last non-blank column / row.
  The fragment shader then draws a backdrop + border box that hugs that area (top-left anchored by default), scales the
  content down only if the box would not fit the quad, and draws nothing at all while every slot is blank.
  Layout is done in "quad space": height = 1, width = the quad's real world aspect (read from unity_ObjectToWorld),
  so padding / border / corner radius are measured in slot WIDTHS and look the same on all four sides whatever the
  quad's proportions are. The mesh is assumed to be a unit quad (Unity's built-in Quad, which KAT installs).

  Anchors are defined on the PHYSICAL quad (X: 0 = the uv.x = 0 end, 1 = the uv.x = 1 end), not on what the viewer
  sees. The shader mirrors the drawing when the quad is seen from behind or in a mirror so the text stays readable,
  and the anchors are mirrored with it, so the box stays at the same place in the world from every side.

SPEECH BUBBLE (head anchor)
  _Tail > 0 switches the box into speech-bubble mode: a tail hangs under the box on the _TailSide end of the quad, the box
  is pinned to that end and to the bottom of the quad (so the tail tip stays where it is and the bubble grows away
  from the head), and _AnchorX / _AnchorY are ignored. The animator drives _Tail and _TailSide from the KAT_HeadAnchor radial: _Tail is 1 across the side-of-head speech-bubble range and fades to 0 for chest/top positions; _TailSide flips on the right-side bubble so the tail still points toward the head.
  Distance grow is pivoted on the TAIL TIP in this mode (the point that touches the head), so the bubble grows away from
  the head instead of swelling around the middle of the quad. Outside speech-bubble mode grow pivots on the quad centre.
*/

Shader "Unlit/KF_VRChatAvatarTextShaderPaged"
{
	Properties
	{
		[Enum(Off,0,Front,1,Back,2)] _Culling ("Culling Mode", Int) = 2
		_GlyphArray ("Glyph SDF Array (one slice per page)", 2DArray) = "" {}
		_ColorArray ("Emoji Array (BC7 or RGBA32, sRGB, one slice per emoji page; optional)", 2DArray) = "" {}
		_ColorPageStart ("First emoji page (pages from here on come from the emoji array; 255 = none)", Range(1, 255)) = 255
		_MainColor ("Main Colour", Color) = (1, 1, 1, 1)
		_ShadowColor ("Outline / Shadow Colour", Color) = (0, 0, 0, 1)
		_OutlineWidth ("Outline Width (SDF units)", Range(0, 0.3)) = 0.0
		_Weight ("Weight (bolder +, thinner -)", Range(-0.15, 0.15)) = 0
		[Space]
		_RowLength ("Text Output Row Length", Float) = 32
		_RowColumns ("Text Output Row Columns", Float) = 4
		[Toggle] _GrowEnabled ("Grow Text with Distance", Range(0, 1)) = 1
		_GrowStart ("Grow Start Distance", Range(0, 10)) = 0.5
		_GrowEnd ("Grow End Distance", Range(0, 10)) = 1.5
		_GrowAmount ("Grow Amount", Range(0, 2)) = 1
		_ExtentScanSlots ("Box Extent Scan Slots (performance)", Range(1, 128)) = 128
		[Space]
		[Toggle] _BoxEnabled ("Show Box (backdrop + border) while text is shown", Float) = 1
		[Toggle] _ShrinkToFit ("Shrink Box To The Text", Float) = 1
		_BackColor ("Box Backdrop Colour", Color) = (0, 0, 0, 0.55)
		_BorderColor ("Box Border Colour", Color) = (1, 1, 1, 0.9)
		_BorderWidth ("Border Width (slot widths)", Range(0, 0.6)) = 0.14
		_Padding ("Box Padding (slot widths)", Range(0, 2)) = 0.5
		_CornerRadius ("Corner Radius (slot widths)", Range(0, 2)) = 0.5
		_Scale ("Text Scale", Range(0.2, 1)) = 1
		_AnchorX ("Anchor X on the quad (0 = uv.x 0 end, 0.5 = centre, 1 = uv.x 1 end)", Range(0, 1)) = 0.5
		_AnchorY ("Anchor Y (0 = top, 1 = bottom)", Range(0, 1)) = 0
		[Toggle] _FaceViewer ("Face Viewer Billboard (animated by KAT_HeadAnchor)", Float) = 0
		[Enum(Auto,0,Always,1,Never,2)] _FaceViewerMode ("Face Viewer Override (Auto lets the head anchor decide)", Float) = 0
		_Tail ("Speech Tail Size (0 = none, animated by KAT_HeadAnchor)", Range(0, 1)) = 0
		_TailWidth ("Speech Tail Width (slot widths)", Range(0.2, 3)) = 1.6
		_TailHeight ("Speech Tail Height (slot widths)", Range(0.2, 4)) = 2
		_TailLean ("Speech Tail Lean", Range(0, 1)) = 0.35
		_TailMargin ("Speech Tail Edge Margin (slot widths)", Range(0, 2)) = 0.4
		_TailSide ("Tail Side on the quad (0 = uv.x 0 end, 1 = uv.x 1 end)", Range(0, 1)) = 0
		[Space]
		// Debug: fills the textbox with a rectangle of a single glyph
		[Toggle] _DebugFillEnabled ("Debug: fill grid with one glyph", Float) = 0
		_DebugFillPage     ("Debug fill: glyph page (0-14; 0-253 in page format 3)", Range(0, 253)) = 1
		_DebugFillSlot     ("Debug fill: glyph slot (0-254)", Range(0, 254))  = 0
		_DebugFillColumns  ("Debug fill: columns",            Range(1, 128))  = 32
		_DebugFillRows     ("Debug fill: rows",               Range(1, 16))   = 4
		[Space]
		[HideInInspector] _Char0("Character 0", Float) = 0
		[HideInInspector] _Char1("Character 1", Float) = 0
		[HideInInspector] _Char2("Character 2", Float) = 0
		[HideInInspector] _Char3("Character 3", Float) = 0
		[HideInInspector] _Char4("Character 4", Float) = 0
		[HideInInspector] _Char5("Character 5", Float) = 0
		[HideInInspector] _Char6("Character 6", Float) = 0
		[HideInInspector] _Char7("Character 7", Float) = 0
		[HideInInspector] _Char8("Character 8", Float) = 0
		[HideInInspector] _Char9("Character 9", Float) = 0
		[HideInInspector] _Char10("Character 10", Float) = 0
		[HideInInspector] _Char11("Character 11", Float) = 0
		[HideInInspector] _Char12("Character 12", Float) = 0
		[HideInInspector] _Char13("Character 13", Float) = 0
		[HideInInspector] _Char14("Character 14", Float) = 0
		[HideInInspector] _Char15("Character 15", Float) = 0
		[HideInInspector] _Char16("Character 16", Float) = 0
		[HideInInspector] _Char17("Character 17", Float) = 0
		[HideInInspector] _Char18("Character 18", Float) = 0
		[HideInInspector] _Char19("Character 19", Float) = 0
		[HideInInspector] _Char20("Character 20", Float) = 0
		[HideInInspector] _Char21("Character 21", Float) = 0
		[HideInInspector] _Char22("Character 22", Float) = 0
		[HideInInspector] _Char23("Character 23", Float) = 0
		[HideInInspector] _Char24("Character 24", Float) = 0
		[HideInInspector] _Char25("Character 25", Float) = 0
		[HideInInspector] _Char26("Character 26", Float) = 0
		[HideInInspector] _Char27("Character 27", Float) = 0
		[HideInInspector] _Char28("Character 28", Float) = 0
		[HideInInspector] _Char29("Character 29", Float) = 0
		[HideInInspector] _Char30("Character 30", Float) = 0
		[HideInInspector] _Char31("Character 31", Float) = 0
		[HideInInspector] _Char32("Character 32", Float) = 0
		[HideInInspector] _Char33("Character 33", Float) = 0
		[HideInInspector] _Char34("Character 34", Float) = 0
		[HideInInspector] _Char35("Character 35", Float) = 0
		[HideInInspector] _Char36("Character 36", Float) = 0
		[HideInInspector] _Char37("Character 37", Float) = 0
		[HideInInspector] _Char38("Character 38", Float) = 0
		[HideInInspector] _Char39("Character 39", Float) = 0
		[HideInInspector] _Char40("Character 40", Float) = 0
		[HideInInspector] _Char41("Character 41", Float) = 0
		[HideInInspector] _Char42("Character 42", Float) = 0
		[HideInInspector] _Char43("Character 43", Float) = 0
		[HideInInspector] _Char44("Character 44", Float) = 0
		[HideInInspector] _Char45("Character 45", Float) = 0
		[HideInInspector] _Char46("Character 46", Float) = 0
		[HideInInspector] _Char47("Character 47", Float) = 0
		[HideInInspector] _Char48("Character 48", Float) = 0
		[HideInInspector] _Char49("Character 49", Float) = 0
		[HideInInspector] _Char50("Character 50", Float) = 0
		[HideInInspector] _Char51("Character 51", Float) = 0
		[HideInInspector] _Char52("Character 52", Float) = 0
		[HideInInspector] _Char53("Character 53", Float) = 0
		[HideInInspector] _Char54("Character 54", Float) = 0
		[HideInInspector] _Char55("Character 55", Float) = 0
		[HideInInspector] _Char56("Character 56", Float) = 0
		[HideInInspector] _Char57("Character 57", Float) = 0
		[HideInInspector] _Char58("Character 58", Float) = 0
		[HideInInspector] _Char59("Character 59", Float) = 0
		[HideInInspector] _Char60("Character 60", Float) = 0
		[HideInInspector] _Char61("Character 61", Float) = 0
		[HideInInspector] _Char62("Character 62", Float) = 0
		[HideInInspector] _Char63("Character 63", Float) = 0
		[HideInInspector] _Char64("Character 64", Float) = 0
		[HideInInspector] _Char65("Character 65", Float) = 0
		[HideInInspector] _Char66("Character 66", Float) = 0
		[HideInInspector] _Char67("Character 67", Float) = 0
		[HideInInspector] _Char68("Character 68", Float) = 0
		[HideInInspector] _Char69("Character 69", Float) = 0
		[HideInInspector] _Char70("Character 70", Float) = 0
		[HideInInspector] _Char71("Character 71", Float) = 0
		[HideInInspector] _Char72("Character 72", Float) = 0
		[HideInInspector] _Char73("Character 73", Float) = 0
		[HideInInspector] _Char74("Character 74", Float) = 0
		[HideInInspector] _Char75("Character 75", Float) = 0
		[HideInInspector] _Char76("Character 76", Float) = 0
		[HideInInspector] _Char77("Character 77", Float) = 0
		[HideInInspector] _Char78("Character 78", Float) = 0
		[HideInInspector] _Char79("Character 79", Float) = 0
		[HideInInspector] _Char80("Character 80", Float) = 0
		[HideInInspector] _Char81("Character 81", Float) = 0
		[HideInInspector] _Char82("Character 82", Float) = 0
		[HideInInspector] _Char83("Character 83", Float) = 0
		[HideInInspector] _Char84("Character 84", Float) = 0
		[HideInInspector] _Char85("Character 85", Float) = 0
		[HideInInspector] _Char86("Character 86", Float) = 0
		[HideInInspector] _Char87("Character 87", Float) = 0
		[HideInInspector] _Char88("Character 88", Float) = 0
		[HideInInspector] _Char89("Character 89", Float) = 0
		[HideInInspector] _Char90("Character 90", Float) = 0
		[HideInInspector] _Char91("Character 91", Float) = 0
		[HideInInspector] _Char92("Character 92", Float) = 0
		[HideInInspector] _Char93("Character 93", Float) = 0
		[HideInInspector] _Char94("Character 94", Float) = 0
		[HideInInspector] _Char95("Character 95", Float) = 0
		[HideInInspector] _Char96("Character 96", Float) = 0
		[HideInInspector] _Char97("Character 97", Float) = 0
		[HideInInspector] _Char98("Character 98", Float) = 0
		[HideInInspector] _Char99("Character 99", Float) = 0
		[HideInInspector] _Char100("Character 100", Float) = 0
		[HideInInspector] _Char101("Character 101", Float) = 0
		[HideInInspector] _Char102("Character 102", Float) = 0
		[HideInInspector] _Char103("Character 103", Float) = 0
		[HideInInspector] _Char104("Character 104", Float) = 0
		[HideInInspector] _Char105("Character 105", Float) = 0
		[HideInInspector] _Char106("Character 106", Float) = 0
		[HideInInspector] _Char107("Character 107", Float) = 0
		[HideInInspector] _Char108("Character 108", Float) = 0
		[HideInInspector] _Char109("Character 109", Float) = 0
		[HideInInspector] _Char110("Character 110", Float) = 0
		[HideInInspector] _Char111("Character 111", Float) = 0
		[HideInInspector] _Char112("Character 112", Float) = 0
		[HideInInspector] _Char113("Character 113", Float) = 0
		[HideInInspector] _Char114("Character 114", Float) = 0
		[HideInInspector] _Char115("Character 115", Float) = 0
		[HideInInspector] _Char116("Character 116", Float) = 0
		[HideInInspector] _Char117("Character 117", Float) = 0
		[HideInInspector] _Char118("Character 118", Float) = 0
		[HideInInspector] _Char119("Character 119", Float) = 0
		[HideInInspector] _Char120("Character 120", Float) = 0
		[HideInInspector] _Char121("Character 121", Float) = 0
		[HideInInspector] _Char122("Character 122", Float) = 0
		[HideInInspector] _Char123("Character 123", Float) = 0
		[HideInInspector] _Char124("Character 124", Float) = 0
		[HideInInspector] _Char125("Character 125", Float) = 0
		[HideInInspector] _Char126("Character 126", Float) = 0
		[HideInInspector] _Char127("Character 127", Float) = 0
		[HideInInspector] _Page0("Page 0 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page1("Page 1 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page2("Page 2 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page3("Page 3 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page4("Page 4 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page5("Page 5 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page6("Page 6 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page7("Page 7 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page8("Page 8 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page9("Page 9 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page10("Page 10 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page11("Page 11 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page12("Page 12 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page13("Page 13 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page14("Page 14 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page15("Page 15 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page16("Page 16 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page17("Page 17 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page18("Page 18 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page19("Page 19 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page20("Page 20 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page21("Page 21 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page22("Page 22 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page23("Page 23 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page24("Page 24 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page25("Page 25 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page26("Page 26 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page27("Page 27 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page28("Page 28 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page29("Page 29 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page30("Page 30 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page31("Page 31 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page32("Page 32 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page33("Page 33 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page34("Page 34 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page35("Page 35 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page36("Page 36 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page37("Page 37 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page38("Page 38 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page39("Page 39 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page40("Page 40 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page41("Page 41 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page42("Page 42 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page43("Page 43 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page44("Page 44 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page45("Page 45 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page46("Page 46 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page47("Page 47 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page48("Page 48 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page49("Page 49 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page50("Page 50 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page51("Page 51 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page52("Page 52 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page53("Page 53 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page54("Page 54 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page55("Page 55 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page56("Page 56 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page57("Page 57 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page58("Page 58 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page59("Page 59 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page60("Page 60 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page61("Page 61 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page62("Page 62 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page63("Page 63 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page64("Page 64 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page65("Page 65 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page66("Page 66 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page67("Page 67 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page68("Page 68 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page69("Page 69 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page70("Page 70 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page71("Page 71 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page72("Page 72 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page73("Page 73 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page74("Page 74 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page75("Page 75 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page76("Page 76 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page77("Page 77 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page78("Page 78 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page79("Page 79 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page80("Page 80 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page81("Page 81 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page82("Page 82 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page83("Page 83 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page84("Page 84 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page85("Page 85 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page86("Page 86 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page87("Page 87 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page88("Page 88 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page89("Page 89 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page90("Page 90 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page91("Page 91 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page92("Page 92 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page93("Page 93 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page94("Page 94 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page95("Page 95 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page96("Page 96 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page97("Page 97 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page98("Page 98 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page99("Page 99 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page100("Page 100 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page101("Page 101 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page102("Page 102 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page103("Page 103 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page104("Page 104 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page105("Page 105 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page106("Page 106 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page107("Page 107 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page108("Page 108 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page109("Page 109 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page110("Page 110 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page111("Page 111 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page112("Page 112 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page113("Page 113 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page114("Page 114 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page115("Page 115 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page116("Page 116 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page117("Page 117 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page118("Page 118 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page119("Page 119 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page120("Page 120 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page121("Page 121 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page122("Page 122 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page123("Page 123 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page124("Page 124 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page125("Page 125 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page126("Page 126 (pair in format 2, slot in format 3)", Float) = 0
		[HideInInspector] _Page127("Page 127 (pair in format 2, slot in format 3)", Float) = 0
	}

	SubShader
	{
		Tags
		{
			"RenderType" = "Overlay"
			"DisableBatching" = "True"
			"IgnoreProjector" = "True"
			"PreviewType" = "Plane"
			"Queue" = "AlphaTest+549"
		}

		LOD 100
		Cull [_Culling]
		ZWrite Off
		AlphaToMask On

		Pass
		{
			Name "FORWARD"
			Tags { "LightMode" = "Always" "Queue" = "Overlay" }
			ZWrite On

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 4.0
			#pragma require 2darray
			#pragma multi_compile_fog
			#pragma multi_compile_instancing
			#pragma shader_feature_local _ KAT_PAGE_FORMAT_3
			#pragma shader_feature_local _ KAT_COLOR_GLYPHS

			#include "UnityCG.cginc"

			UNITY_DECLARE_TEX2DARRAY(_GlyphArray);
#ifdef KAT_COLOR_GLYPHS
			UNITY_DECLARE_TEX2DARRAY(_ColorArray);
#endif
			float _ColorPageStart;

			float4 _MainColor;
			float4 _ShadowColor;
			float _OutlineWidth;
			float _Weight;
			float _RowLength;
			float _RowColumns;
			float _GrowEnabled;
			float _GrowStart;
			float _GrowEnd;
			float _GrowAmount;
			float _ExtentScanSlots;
			float _BoxEnabled;
			float _ShrinkToFit;
			float4 _BackColor;
			float4 _BorderColor;
			float _BorderWidth;
			float _Padding;
			float _CornerRadius;
			float _Scale;
			float _AnchorX;
			float _AnchorY;
			float _FaceViewer;
			float _FaceViewerMode;
			float _Tail;
			float _TailWidth;
			float _TailHeight;
			float _TailLean;
			float _TailMargin;
			float _TailSide;
			float _DebugFillEnabled;
			float _DebugFillPage;
			float _DebugFillSlot;
			float _DebugFillColumns;
			float _DebugFillRows;

			CBUFFER_START(CharacterBuffer)
				float4 _CharPack[32] : packoffset(c0);
#ifdef KAT_PAGE_FORMAT_3
				float4 _PagePack[32] : packoffset(c32);
#else
				float4 _PagePack[16] : packoffset(c32);
#endif
			float _Char0 : packoffset(c0.x);
			float _Char1 : packoffset(c0.y);
			float _Char2 : packoffset(c0.z);
			float _Char3 : packoffset(c0.w);
			float _Char4 : packoffset(c1.x);
			float _Char5 : packoffset(c1.y);
			float _Char6 : packoffset(c1.z);
			float _Char7 : packoffset(c1.w);
			float _Char8 : packoffset(c2.x);
			float _Char9 : packoffset(c2.y);
			float _Char10 : packoffset(c2.z);
			float _Char11 : packoffset(c2.w);
			float _Char12 : packoffset(c3.x);
			float _Char13 : packoffset(c3.y);
			float _Char14 : packoffset(c3.z);
			float _Char15 : packoffset(c3.w);
			float _Char16 : packoffset(c4.x);
			float _Char17 : packoffset(c4.y);
			float _Char18 : packoffset(c4.z);
			float _Char19 : packoffset(c4.w);
			float _Char20 : packoffset(c5.x);
			float _Char21 : packoffset(c5.y);
			float _Char22 : packoffset(c5.z);
			float _Char23 : packoffset(c5.w);
			float _Char24 : packoffset(c6.x);
			float _Char25 : packoffset(c6.y);
			float _Char26 : packoffset(c6.z);
			float _Char27 : packoffset(c6.w);
			float _Char28 : packoffset(c7.x);
			float _Char29 : packoffset(c7.y);
			float _Char30 : packoffset(c7.z);
			float _Char31 : packoffset(c7.w);
			float _Char32 : packoffset(c8.x);
			float _Char33 : packoffset(c8.y);
			float _Char34 : packoffset(c8.z);
			float _Char35 : packoffset(c8.w);
			float _Char36 : packoffset(c9.x);
			float _Char37 : packoffset(c9.y);
			float _Char38 : packoffset(c9.z);
			float _Char39 : packoffset(c9.w);
			float _Char40 : packoffset(c10.x);
			float _Char41 : packoffset(c10.y);
			float _Char42 : packoffset(c10.z);
			float _Char43 : packoffset(c10.w);
			float _Char44 : packoffset(c11.x);
			float _Char45 : packoffset(c11.y);
			float _Char46 : packoffset(c11.z);
			float _Char47 : packoffset(c11.w);
			float _Char48 : packoffset(c12.x);
			float _Char49 : packoffset(c12.y);
			float _Char50 : packoffset(c12.z);
			float _Char51 : packoffset(c12.w);
			float _Char52 : packoffset(c13.x);
			float _Char53 : packoffset(c13.y);
			float _Char54 : packoffset(c13.z);
			float _Char55 : packoffset(c13.w);
			float _Char56 : packoffset(c14.x);
			float _Char57 : packoffset(c14.y);
			float _Char58 : packoffset(c14.z);
			float _Char59 : packoffset(c14.w);
			float _Char60 : packoffset(c15.x);
			float _Char61 : packoffset(c15.y);
			float _Char62 : packoffset(c15.z);
			float _Char63 : packoffset(c15.w);
			float _Char64 : packoffset(c16.x);
			float _Char65 : packoffset(c16.y);
			float _Char66 : packoffset(c16.z);
			float _Char67 : packoffset(c16.w);
			float _Char68 : packoffset(c17.x);
			float _Char69 : packoffset(c17.y);
			float _Char70 : packoffset(c17.z);
			float _Char71 : packoffset(c17.w);
			float _Char72 : packoffset(c18.x);
			float _Char73 : packoffset(c18.y);
			float _Char74 : packoffset(c18.z);
			float _Char75 : packoffset(c18.w);
			float _Char76 : packoffset(c19.x);
			float _Char77 : packoffset(c19.y);
			float _Char78 : packoffset(c19.z);
			float _Char79 : packoffset(c19.w);
			float _Char80 : packoffset(c20.x);
			float _Char81 : packoffset(c20.y);
			float _Char82 : packoffset(c20.z);
			float _Char83 : packoffset(c20.w);
			float _Char84 : packoffset(c21.x);
			float _Char85 : packoffset(c21.y);
			float _Char86 : packoffset(c21.z);
			float _Char87 : packoffset(c21.w);
			float _Char88 : packoffset(c22.x);
			float _Char89 : packoffset(c22.y);
			float _Char90 : packoffset(c22.z);
			float _Char91 : packoffset(c22.w);
			float _Char92 : packoffset(c23.x);
			float _Char93 : packoffset(c23.y);
			float _Char94 : packoffset(c23.z);
			float _Char95 : packoffset(c23.w);
			float _Char96 : packoffset(c24.x);
			float _Char97 : packoffset(c24.y);
			float _Char98 : packoffset(c24.z);
			float _Char99 : packoffset(c24.w);
			float _Char100 : packoffset(c25.x);
			float _Char101 : packoffset(c25.y);
			float _Char102 : packoffset(c25.z);
			float _Char103 : packoffset(c25.w);
			float _Char104 : packoffset(c26.x);
			float _Char105 : packoffset(c26.y);
			float _Char106 : packoffset(c26.z);
			float _Char107 : packoffset(c26.w);
			float _Char108 : packoffset(c27.x);
			float _Char109 : packoffset(c27.y);
			float _Char110 : packoffset(c27.z);
			float _Char111 : packoffset(c27.w);
			float _Char112 : packoffset(c28.x);
			float _Char113 : packoffset(c28.y);
			float _Char114 : packoffset(c28.z);
			float _Char115 : packoffset(c28.w);
			float _Char116 : packoffset(c29.x);
			float _Char117 : packoffset(c29.y);
			float _Char118 : packoffset(c29.z);
			float _Char119 : packoffset(c29.w);
			float _Char120 : packoffset(c30.x);
			float _Char121 : packoffset(c30.y);
			float _Char122 : packoffset(c30.z);
			float _Char123 : packoffset(c30.w);
			float _Char124 : packoffset(c31.x);
			float _Char125 : packoffset(c31.y);
			float _Char126 : packoffset(c31.z);
			float _Char127 : packoffset(c31.w);
			float _Page0 : packoffset(c32.x);
			float _Page1 : packoffset(c32.y);
			float _Page2 : packoffset(c32.z);
			float _Page3 : packoffset(c32.w);
			float _Page4 : packoffset(c33.x);
			float _Page5 : packoffset(c33.y);
			float _Page6 : packoffset(c33.z);
			float _Page7 : packoffset(c33.w);
			float _Page8 : packoffset(c34.x);
			float _Page9 : packoffset(c34.y);
			float _Page10 : packoffset(c34.z);
			float _Page11 : packoffset(c34.w);
			float _Page12 : packoffset(c35.x);
			float _Page13 : packoffset(c35.y);
			float _Page14 : packoffset(c35.z);
			float _Page15 : packoffset(c35.w);
			float _Page16 : packoffset(c36.x);
			float _Page17 : packoffset(c36.y);
			float _Page18 : packoffset(c36.z);
			float _Page19 : packoffset(c36.w);
			float _Page20 : packoffset(c37.x);
			float _Page21 : packoffset(c37.y);
			float _Page22 : packoffset(c37.z);
			float _Page23 : packoffset(c37.w);
			float _Page24 : packoffset(c38.x);
			float _Page25 : packoffset(c38.y);
			float _Page26 : packoffset(c38.z);
			float _Page27 : packoffset(c38.w);
			float _Page28 : packoffset(c39.x);
			float _Page29 : packoffset(c39.y);
			float _Page30 : packoffset(c39.z);
			float _Page31 : packoffset(c39.w);
			float _Page32 : packoffset(c40.x);
			float _Page33 : packoffset(c40.y);
			float _Page34 : packoffset(c40.z);
			float _Page35 : packoffset(c40.w);
			float _Page36 : packoffset(c41.x);
			float _Page37 : packoffset(c41.y);
			float _Page38 : packoffset(c41.z);
			float _Page39 : packoffset(c41.w);
			float _Page40 : packoffset(c42.x);
			float _Page41 : packoffset(c42.y);
			float _Page42 : packoffset(c42.z);
			float _Page43 : packoffset(c42.w);
			float _Page44 : packoffset(c43.x);
			float _Page45 : packoffset(c43.y);
			float _Page46 : packoffset(c43.z);
			float _Page47 : packoffset(c43.w);
			float _Page48 : packoffset(c44.x);
			float _Page49 : packoffset(c44.y);
			float _Page50 : packoffset(c44.z);
			float _Page51 : packoffset(c44.w);
			float _Page52 : packoffset(c45.x);
			float _Page53 : packoffset(c45.y);
			float _Page54 : packoffset(c45.z);
			float _Page55 : packoffset(c45.w);
			float _Page56 : packoffset(c46.x);
			float _Page57 : packoffset(c46.y);
			float _Page58 : packoffset(c46.z);
			float _Page59 : packoffset(c46.w);
			float _Page60 : packoffset(c47.x);
			float _Page61 : packoffset(c47.y);
			float _Page62 : packoffset(c47.z);
			float _Page63 : packoffset(c47.w);
#ifdef KAT_PAGE_FORMAT_3
			float _Page64 : packoffset(c48.x);
			float _Page65 : packoffset(c48.y);
			float _Page66 : packoffset(c48.z);
			float _Page67 : packoffset(c48.w);
			float _Page68 : packoffset(c49.x);
			float _Page69 : packoffset(c49.y);
			float _Page70 : packoffset(c49.z);
			float _Page71 : packoffset(c49.w);
			float _Page72 : packoffset(c50.x);
			float _Page73 : packoffset(c50.y);
			float _Page74 : packoffset(c50.z);
			float _Page75 : packoffset(c50.w);
			float _Page76 : packoffset(c51.x);
			float _Page77 : packoffset(c51.y);
			float _Page78 : packoffset(c51.z);
			float _Page79 : packoffset(c51.w);
			float _Page80 : packoffset(c52.x);
			float _Page81 : packoffset(c52.y);
			float _Page82 : packoffset(c52.z);
			float _Page83 : packoffset(c52.w);
			float _Page84 : packoffset(c53.x);
			float _Page85 : packoffset(c53.y);
			float _Page86 : packoffset(c53.z);
			float _Page87 : packoffset(c53.w);
			float _Page88 : packoffset(c54.x);
			float _Page89 : packoffset(c54.y);
			float _Page90 : packoffset(c54.z);
			float _Page91 : packoffset(c54.w);
			float _Page92 : packoffset(c55.x);
			float _Page93 : packoffset(c55.y);
			float _Page94 : packoffset(c55.z);
			float _Page95 : packoffset(c55.w);
			float _Page96 : packoffset(c56.x);
			float _Page97 : packoffset(c56.y);
			float _Page98 : packoffset(c56.z);
			float _Page99 : packoffset(c56.w);
			float _Page100 : packoffset(c57.x);
			float _Page101 : packoffset(c57.y);
			float _Page102 : packoffset(c57.z);
			float _Page103 : packoffset(c57.w);
			float _Page104 : packoffset(c58.x);
			float _Page105 : packoffset(c58.y);
			float _Page106 : packoffset(c58.z);
			float _Page107 : packoffset(c58.w);
			float _Page108 : packoffset(c59.x);
			float _Page109 : packoffset(c59.y);
			float _Page110 : packoffset(c59.z);
			float _Page111 : packoffset(c59.w);
			float _Page112 : packoffset(c60.x);
			float _Page113 : packoffset(c60.y);
			float _Page114 : packoffset(c60.z);
			float _Page115 : packoffset(c60.w);
			float _Page116 : packoffset(c61.x);
			float _Page117 : packoffset(c61.y);
			float _Page118 : packoffset(c61.z);
			float _Page119 : packoffset(c61.w);
			float _Page120 : packoffset(c62.x);
			float _Page121 : packoffset(c62.y);
			float _Page122 : packoffset(c62.z);
			float _Page123 : packoffset(c62.w);
			float _Page124 : packoffset(c63.x);
			float _Page125 : packoffset(c63.y);
			float _Page126 : packoffset(c63.z);
			float _Page127 : packoffset(c63.w);
#endif
			CBUFFER_END

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct v2f
			{
				float2 uv : TEXCOORD0;
				UNITY_FOG_COORDS(1)
				float4 vertex : SV_POSITION;
				// x = last used column, y = last used row (-1 when every slot is blank), z = quad width / height
				nointerpolation float3 info : TEXCOORD2;
				UNITY_VERTEX_INPUT_INSTANCE_ID   // UNITY_TRANSFER_INSTANCE_ID in vert() writes o.instanceID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			// Touches every animated property so Unity keeps sending them.
			float sumSlots()
			{
				float total =
			_Char0 + _Char1 + _Char2 + _Char3 + _Char4 + _Char5 + _Char6 + _Char7 +
			_Char8 + _Char9 + _Char10 + _Char11 + _Char12 + _Char13 + _Char14 + _Char15 +
			_Char16 + _Char17 + _Char18 + _Char19 + _Char20 + _Char21 + _Char22 + _Char23 +
			_Char24 + _Char25 + _Char26 + _Char27 + _Char28 + _Char29 + _Char30 + _Char31 +
			_Char32 + _Char33 + _Char34 + _Char35 + _Char36 + _Char37 + _Char38 + _Char39 +
			_Char40 + _Char41 + _Char42 + _Char43 + _Char44 + _Char45 + _Char46 + _Char47 +
			_Char48 + _Char49 + _Char50 + _Char51 + _Char52 + _Char53 + _Char54 + _Char55 +
			_Char56 + _Char57 + _Char58 + _Char59 + _Char60 + _Char61 + _Char62 + _Char63 +
			_Char64 + _Char65 + _Char66 + _Char67 + _Char68 + _Char69 + _Char70 + _Char71 +
			_Char72 + _Char73 + _Char74 + _Char75 + _Char76 + _Char77 + _Char78 + _Char79 +
			_Char80 + _Char81 + _Char82 + _Char83 + _Char84 + _Char85 + _Char86 + _Char87 +
			_Char88 + _Char89 + _Char90 + _Char91 + _Char92 + _Char93 + _Char94 + _Char95 +
			_Char96 + _Char97 + _Char98 + _Char99 + _Char100 + _Char101 + _Char102 + _Char103 +
			_Char104 + _Char105 + _Char106 + _Char107 + _Char108 + _Char109 + _Char110 + _Char111 +
			_Char112 + _Char113 + _Char114 + _Char115 + _Char116 + _Char117 + _Char118 + _Char119 +
			_Char120 + _Char121 + _Char122 + _Char123 + _Char124 + _Char125 + _Char126 + _Char127;
#ifdef KAT_PAGE_FORMAT_3
				total +=
			_Page0 + _Page1 + _Page2 + _Page3 + _Page4 + _Page5 + _Page6 + _Page7 +
			_Page8 + _Page9 + _Page10 + _Page11 + _Page12 + _Page13 + _Page14 + _Page15 +
			_Page16 + _Page17 + _Page18 + _Page19 + _Page20 + _Page21 + _Page22 + _Page23 +
			_Page24 + _Page25 + _Page26 + _Page27 + _Page28 + _Page29 + _Page30 + _Page31 +
			_Page32 + _Page33 + _Page34 + _Page35 + _Page36 + _Page37 + _Page38 + _Page39 +
			_Page40 + _Page41 + _Page42 + _Page43 + _Page44 + _Page45 + _Page46 + _Page47 +
			_Page48 + _Page49 + _Page50 + _Page51 + _Page52 + _Page53 + _Page54 + _Page55 +
			_Page56 + _Page57 + _Page58 + _Page59 + _Page60 + _Page61 + _Page62 + _Page63 +
			_Page64 + _Page65 + _Page66 + _Page67 + _Page68 + _Page69 + _Page70 + _Page71 +
			_Page72 + _Page73 + _Page74 + _Page75 + _Page76 + _Page77 + _Page78 + _Page79 +
			_Page80 + _Page81 + _Page82 + _Page83 + _Page84 + _Page85 + _Page86 + _Page87 +
			_Page88 + _Page89 + _Page90 + _Page91 + _Page92 + _Page93 + _Page94 + _Page95 +
			_Page96 + _Page97 + _Page98 + _Page99 + _Page100 + _Page101 + _Page102 + _Page103 +
			_Page104 + _Page105 + _Page106 + _Page107 + _Page108 + _Page109 + _Page110 + _Page111 +
			_Page112 + _Page113 + _Page114 + _Page115 + _Page116 + _Page117 + _Page118 + _Page119 +
			_Page120 + _Page121 + _Page122 + _Page123 + _Page124 + _Page125 + _Page126 + _Page127;
#else
				total +=
			_Page0 + _Page1 + _Page2 + _Page3 + _Page4 + _Page5 + _Page6 + _Page7 +
			_Page8 + _Page9 + _Page10 + _Page11 + _Page12 + _Page13 + _Page14 + _Page15 +
			_Page16 + _Page17 + _Page18 + _Page19 + _Page20 + _Page21 + _Page22 + _Page23 +
			_Page24 + _Page25 + _Page26 + _Page27 + _Page28 + _Page29 + _Page30 + _Page31 +
			_Page32 + _Page33 + _Page34 + _Page35 + _Page36 + _Page37 + _Page38 + _Page39 +
			_Page40 + _Page41 + _Page42 + _Page43 + _Page44 + _Page45 + _Page46 + _Page47 +
			_Page48 + _Page49 + _Page50 + _Page51 + _Page52 + _Page53 + _Page54 + _Page55 +
			_Page56 + _Page57 + _Page58 + _Page59 + _Page60 + _Page61 + _Page62 + _Page63;
#endif
				return total;
			}

			uint DecodeSymbol(float k)
			{
				k = round(k);
				return (uint)(k < 0.0 ? k + 255.0 : k);
			}

			float ReadPackedScalar(float4 pack, int component)
			{
				if (component == 0) return pack.x;
				if (component == 1) return pack.y;
				if (component == 2) return pack.z;
				return pack.w;
			}

			float ReadCharValue(int slot)
			{
				return ReadPackedScalar(_CharPack[slot >> 2], slot & 3);
			}

			float ReadPageValue(int slot)
			{
				return ReadPackedScalar(_PagePack[slot >> 2], slot & 3);
			}

			void ReadSlot(int slot, out uint page, out uint sym)
			{
				sym = DecodeSymbol(ReadCharValue(slot));
#ifdef KAT_PAGE_FORMAT_3
				// Format 3: one page per slot.
				page = DecodeSymbol(ReadPageValue(slot));
#else
				// Format 2: base-15 pair packing, pages 0..14. One page cell stores two slots.
				uint pair = DecodeSymbol(ReadPageValue(slot >> 1));
				page = ((slot & 1) != 0) ? (pair % 15u) : (pair / 15u);
#endif
			}

			float2 UsedExtent()
			{
				uint rowLen = max((int)(_RowLength + 0.5), 1);
				uint rowCount = max((int)(_RowColumns + 0.5), 1);
				int total = min(rowLen * rowCount, min((int)(_ExtentScanSlots + 0.5), 128));
				int lastCol = -1;
				int lastRow = -1;
				[loop]
				for (int s = 0; s < total; s++)
				{
					uint pg, sy;
					ReadSlot(s, pg, sy);
					if (pg != 0u || sy != 0u)
					{
						int r = s / rowLen;
						lastRow = max(lastRow, r);
						lastCol = max(lastCol, s - r * rowLen);
					}
				}
				return float2((float)lastCol, (float)lastRow);
			}

			// Signed distance to triangle abc, negative inside (Inigo Quilez).
			float TriangleSdf(float2 p, float2 a, float2 b, float2 c)
			{
				float2 e0 = b - a, e1 = c - b, e2 = a - c;
				float2 v0 = p - a, v1 = p - b, v2 = p - c;
				float2 pq0 = v0 - e0 * saturate(dot(v0, e0) / dot(e0, e0));
				float2 pq1 = v1 - e1 * saturate(dot(v1, e1) / dot(e1, e1));
				float2 pq2 = v2 - e2 * saturate(dot(v2, e2) / dot(e2, e2));
				float k = sign(e0.x * e2.y - e0.y * e2.x);
				float2 d = min(min(float2(dot(pq0, pq0), k * (v0.x * e0.y - v0.y * e0.x)),
				                   float2(dot(pq1, pq1), k * (v1.x * e1.y - v1.y * e1.x))),
				                   float2(dot(pq2, pq2), k * (v2.x * e2.y - v2.y * e2.x)));
				return -sqrt(d.x) * sign(d.y);
			}

			// "Quad space": height 1, width = the quad's world aspect, origin top-left, y down.
			struct BoxLayout
			{
				float2 grid;
				float2 quadSize;
				float2 cellSize;
				float2 used;
				float pad;
				float2 boxSize;
				bool bubble;
				float tailH;
				float tailW;
				float2 fullSize;
				float ax;
				float ay;
				float tailSide;
				float s;
				float2 boxOrigin;
			};

			// info = (last used column, last used row, quad width / height). flipU mirrors the anchors (see header).
			BoxLayout ComputeBoxLayout(float3 info, bool flipU)
			{
				BoxLayout L;
				float aspect = max(info.z, 1e-3);
				bool hasText = info.x >= 0.0;
				bool boxOn = _BoxEnabled > 0.5;
				L.grid = float2(_RowLength, _RowColumns);
				L.quadSize = float2(aspect, 1.0);
				L.cellSize = L.quadSize / L.grid; // one slot
				L.used = (_ShrinkToFit > 0.5 && hasText) ? (info.xy + 1.0) : L.grid; // columns, rows covered
				L.pad = boxOn ? _Padding * L.cellSize.x : 0.0;
				L.boxSize = L.used * L.cellSize + 2.0 * L.pad;

				// Speech bubble: the tail hangs under the box, so reserve its height inside the quad.
				L.bubble = boxOn && hasText && _Tail > 0.01;
				L.tailH = L.bubble ? _Tail * _TailHeight * L.cellSize.x : 0.0;
				L.tailW = _Tail * _TailWidth * L.cellSize.x;
				L.fullSize = L.boxSize + float2(0.0, L.tailH);

				// Anchors live on the physical quad; mirror them when the drawing is mirrored (see header note).
				L.ax = L.bubble ? _TailSide : _AnchorX;
				L.ay = L.bubble ? 1.0 : _AnchorY;
				L.tailSide = _TailSide;
				if (flipU) { L.ax = 1.0 - L.ax; L.tailSide = 1.0 - L.tailSide; }

				L.s = min(1.0, min(L.quadSize.x / L.fullSize.x, L.quadSize.y / L.fullSize.y)) * _Scale;
				L.boxOrigin = (L.quadSize - L.fullSize * L.s) * float2(L.ax, L.ay);
				return L;
			}

			float BoxRadius(BoxLayout L)
			{
				float2 boxHalf = L.boxSize * 0.5;
				return min(_CornerRadius * L.cellSize.x, min(boxHalf.x, boxHalf.y));
			}

			// Tail base centre (x, box space) and how far the tip leans away from it.
			void TailPlacement(BoxLayout L, out float bx, out float lean)
			{
				float margin = BoxRadius(L) + _TailMargin * L.cellSize.x;
				float lo = min(margin + L.tailW * 0.5, L.boxSize.x * 0.5);
				float hi = max(L.boxSize.x - margin - L.tailW * 0.5, L.boxSize.x * 0.5);
				bx = L.tailSide < 0.5 ? lo : hi;
				lean = (L.tailSide < 0.5 ? -_TailLean : _TailLean) * L.tailW;
			}

			// Object-space position of the speech-bubble tail tip. Assumes the unit quad (object xy = uv - 0.5).
			//Mirroring the drawing mirrors the anchors with it, so the tip stays on the same physical spot
			// from every side and the unmirrored layout is all that is needed here.
			float3 BubbleTipObject(BoxLayout L)
			{
				float bx, lean;
				TailPlacement(L, bx, lean);
				float2 P = L.boxOrigin + float2(bx + lean, L.boxSize.y + L.tailH) * L.s;
				return float3(P.x / L.quadSize.x - 0.5, 0.5 - P.y, 0.0);
			}

			v2f vert (appdata v)
			{
				v2f o;
				UNITY_INITIALIZE_OUTPUT(v2f, o);
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				// Real world proportions of the quad, so the box can use equal padding on every side.
				float3 axisX = unity_ObjectToWorld._11_21_31;
				float3 axisY = unity_ObjectToWorld._12_22_32;
				float aspect = length(axisX) / max(length(axisY), 1e-5);
				float2 extent = UsedExtent();
				if (_DebugFillEnabled > 0.5)
				{
					// Debug fill must not be suppressed by an empty/unknown character state.
					extent = float2(max(_RowLength, 1.0) - 1.0, max(_RowColumns, 1.0) - 1.0);
				}
				o.info = float3(extent, aspect);

				if (_GrowEnabled > 0.5)
				{
					float3 centerEye = _WorldSpaceCameraPos;
					#ifdef USING_STEREO_MATRICES
					centerEye = 0.5 * (unity_StereoWorldSpaceCameraPos[0] + unity_StereoWorldSpaceCameraPos[1]);
					#endif
					float3 objPos = unity_ObjectToWorld._14_24_34;
					float growEnd = max(_GrowEnd, _GrowStart + 0.0001);
					float grow = smoothstep(_GrowStart, growEnd, distance(centerEye, objPos));

					// Grow about the quad centre, except in speech-bubble mode
					BoxLayout boxL = ComputeBoxLayout(o.info, false);
					float3 pivot = boxL.bubble ? BubbleTipObject(boxL) : float3(0.0, 0.0, 0.0);
					v.vertex.xyz = pivot + (v.vertex.xyz - pivot) * (1.0 + _GrowAmount * grow);
				}


				float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
				// _FaceViewerMode: 0 = Auto (the head anchor clips set _FaceViewer), 1 = Always, 2 = Never.
				// Speech-bubble positions (_Tail > 0) never billboard, because the tail is built for the quad's real orientation.
				float faceViewerBase = _FaceViewerMode > 1.5 ? 0.0 : (_FaceViewerMode > 0.5 ? 1.0 : saturate(_FaceViewer));
				float faceViewerAmount =
					faceViewerBase * (1.0 - saturate(_Tail));

				if (faceViewerAmount > 0.0001)
				{
					float3 cameraRight;
					float3 cameraUp;

					#ifdef USING_STEREO_MATRICES
						cameraRight = normalize(
							unity_StereoMatrixInvV[0]._11_21_31 +
							unity_StereoMatrixInvV[1]._11_21_31
						);

						cameraUp = normalize(
							unity_StereoMatrixInvV[0]._12_22_32 +
							unity_StereoMatrixInvV[1]._12_22_32
						);
					#else
						cameraRight = normalize(UNITY_MATRIX_I_V._11_21_31);
						cameraUp = normalize(UNITY_MATRIX_I_V._12_22_32);
					#endif

					float3 objectCenter = unity_ObjectToWorld._14_24_34;

					float3 billboardPos =
						objectCenter
						+ cameraRight * (v.vertex.x * length(axisX))
						+ cameraUp   * (v.vertex.y * length(axisY));

					worldPos = lerp(
						worldPos,
						billboardPos,
						faceViewerAmount
					);
				}

				o.vertex = UnityWorldToClipPos(worldPos);
				o.uv = v.uv;
				UNITY_TRANSFER_FOG(o, o.vertex);
				return o;
			}

			fixed4 frag (v2f i, uint facing : SV_IsFrontFace) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
				float4 posCS = i.vertex;

				// Flip text when looking at the back face / through a mirror
				bool flipU = false;
				if (!facing) flipU = !flipU;
				if (unity_CameraProjection[2][0] != 0.0 || unity_CameraProjection[2][1] != 0.0) flipU = !flipU;
				if (flipU) i.uv.x = 1.0 - i.uv.x;

				float aspect = max(i.info.z, 1e-3);
				bool hasTextForLayout = i.info.x >= 0.0;
				BoxLayout L = ComputeBoxLayout(i.info, flipU);
				float2 grid = L.grid;
				float2 cellSize = L.cellSize;
				float2 boxSize = L.boxSize;
				float pad = L.pad;
				bool bubble = L.bubble;
				bool boxOn = _BoxEnabled > 0.5;

				float2 P = float2(i.uv.x * aspect, 1.0 - i.uv.y);
				float2 Q = (P - L.boxOrigin) / L.s; // box space before scaling
				float2 g = (Q - pad) / cellSize; // slot-grid coordinates

				float2 gdx = ddx(g);
				float2 gdy = ddy(g);

				float2 cell = floor(g);
				float2 local = g - cell;
				int slot = (int)clamp(cell.x + cell.y * grid.x, 0.0, 127.0);

				bool inGrid = g.x >= 0.0 && g.x < grid.x && g.y >= 0.0 && g.y < grid.y;

				uint page, sym;
				ReadSlot(slot, page, sym);

				// IMPORTANT: glyph visibility is per-cell. Do not gate every fragment on the
				// vertex-side extent scan; that scan is only a box-sizing optimization.
				bool blank = !inGrid;
				bool rightHalf = false;

				bool debugOverride = false;
				if (_DebugFillEnabled > 0.5)
				{
					int cols = min((int)clamp(_DebugFillColumns + 0.5, 1.0, 128.0), max((int)(_RowLength + 0.5), 1));
					int rows = min((int)clamp(_DebugFillRows    + 0.5, 1.0, 16.0),  max((int)(_RowColumns + 0.5), 1));
					int col = (int)cell.x;
					int row = (int)cell.y;
					if (col >= 0 && col < cols && row >= 0 && row < rows)
					{
#ifdef KAT_PAGE_FORMAT_3
						page = (uint)clamp(_DebugFillPage + 0.5, 0.0, 253.0);
#else
						page = (uint)clamp(_DebugFillPage + 0.5, 0.0, 14.0);
#endif
						sym  = (uint)clamp(_DebugFillSlot + 0.5, 0.0, 254.0);
						blank = false;
						debugOverride = true;
					}
					else
					{
						blank = true;
					}
				}
				if (!debugOverride && page == 0u && sym == 254u)
				{
					// FOLLOW: draw the right half of the wide glyph stored in the previous slot
					if (cell.x < 0.5 || slot == 0)
					{
						blank = true;
					}
					else
					{
						ReadSlot(slot - 1, page, sym);
						rightHalf = true;
						if (page == 0u) blank = true;
					}
				}

				float2 uvDown;      // atlas uv, y down
				float2 uvPerSlot;   // atlas uv change per slot unit
				if (page == 0u)
				{
					if (sym >= 128u) blank = true;
					uvDown = float2(((float)(sym & 15u) + local.x) / 16.0, ((float)(sym >> 4) + local.y) / 8.0);
					uvPerSlot = float2(1.0 / 16.0, 1.0 / 8.0);
				}
				else
				{
					uvDown = float2(((float)(sym & 15u) + (rightHalf ? 0.5 : 0.0) + local.x * 0.5) / 16.0,
					                ((float)(sym >> 4) + local.y) / 16.0);
					uvPerSlot = float2(0.5 / 16.0, 1.0 / 16.0);
				}

				// Emoji pages: anything from _ColorPageStart up is read from _ColorArray (slice = page - start) instead of the SDF array.
				bool isColor = false;
#ifdef KAT_COLOR_GLYPHS
				uint colorStart = (uint)(_ColorPageStart + 0.5);
				isColor = !blank && page != 0u && page >= colorStart;
#endif

				// Blank slots sample the empty space glyph (cell 0 of page 0) so derivatives stay well defined
				float3 uvw = float3(uvDown.x, 1.0 - uvDown.y, (float)page);
				if (blank || isColor) uvw = float3(0.001, 0.999, 0.0);

				float2 dX = gdx * float2(uvPerSlot.x, -uvPerSlot.y);
				float2 dY = gdy * float2(uvPerSlot.x, -uvPerSlot.y);
				float sd = _GlyphArray.SampleGrad(sampler_GlyphArray, uvw, dX, dY).r;
				float aa = max(fwidth(sd), 1e-3);
				if (blank || isColor) sd = 0.0;
				float4 glyphColor = float4(0.0, 0.0, 0.0, 0.0);
#ifdef KAT_COLOR_GLYPHS
				[branch]
				if (isColor)
				{
					float3 cuvw = float3(uvDown.x, 1.0 - uvDown.y, (float)(page - colorStart));
					glyphColor = _ColorArray.SampleGrad(sampler_ColorArray, cuvw, dX, dY);
				}
#endif

				float fill = saturate((sd - (0.5 - _Weight)) / aa + 0.5);
				float outer = saturate((sd - (0.5 - _Weight - _OutlineWidth)) / aa + 0.5);

				float3 textRgb = lerp(_ShadowColor.rgb, _MainColor.rgb, fill / max(outer, 1e-4));
				float textA = outer * _MainColor.a;
				if (isColor)
				{
					// Emoji keep their own colours, and only the text colour's alpha applies. The alpha is straight, same as the SDF path.
					textRgb = glyphColor.rgb;
					textA = glyphColor.a * _MainColor.a;
				}

				// Box: rounded rectangle, negative distance inside. Backdrop inside, border in the outer strip.
				float2 boxHalf = boxSize * 0.5;
				float radius = BoxRadius(L);
				float2 dq = abs(Q - boxHalf) - (boxHalf - radius);
				float dBox = length(max(dq, 0.0)) + min(max(dq.x, dq.y), 0.0) - radius;
				if (bubble)
				{
					// Triangle under the bottom edge, near the corner on the tail side, tip leaning outwards.
					float bx, lean;
					TailPlacement(L, bx, lean);
					float2 tA = float2(bx - L.tailW * 0.5, boxSize.y - 1e-4);
					float2 tB = float2(bx + L.tailW * 0.5, boxSize.y - 1e-4);
					float2 tC = float2(bx + lean, boxSize.y + L.tailH);
					dBox = min(dBox, TriangleSdf(Q, tA, tB, tC));
				}
				float aaBox = max(fwidth(dBox), 1e-6);
				float boxCover = saturate(0.5 - dBox / aaBox);
				float borderCover = saturate((dBox + _BorderWidth * cellSize.x) / aaBox + 0.5);
				float4 boxCol = lerp(_BackColor, _BorderColor, borderCover);
				// The box is allowed only when the extent scan found text. Glyph rendering is
				// independent of that scan and therefore remains visible even if the scan is disabled
				// or its budget is smaller than the number of active slots.
				boxCol.a *= boxCover * ((boxOn && hasTextForLayout) ? 1.0 : 0.0);

				// Text over box
				fixed4 col;
				col.a = textA + boxCol.a * (1.0 - textA);
				col.rgb = (textRgb * textA + boxCol.rgb * boxCol.a * (1.0 - textA)) / max(col.a, 1e-5);
				UNITY_APPLY_FOG(i.fogCoord, col);

				if (col.a <= 0.0) discard;

				// Only here so the compiler cannot strip the animated properties.
				[branch]
				if ((col.a + posCS.w) == 65536) return sumSlots();

				return col;
			}
			ENDCG
		}
	}
	FallBack "Unlit/Transparent"
}
