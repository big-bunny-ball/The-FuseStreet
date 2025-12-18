/* using System;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace TheFuseStreet.Scripts;

public partial class MainInterface : Control
{
    [Export] public TextEdit UserDesTextEdit;
    [Export] public LineEdit Culture1LineEdit;
    [Export] public LineEdit Culture2LineEdit;

    // === ALL THESE BUTTONS NEED TO BE CHANGE TO TEXTUREBUTTON WHEN UI DESIGN IS FINALISED!
    [Export] public Button PlayButton;
    [Export] public TextureButton ToggleButton;
    [Export] public Button ResetButton;
    [Export] public Button ScreenshotButton;

    // http req nodes
    private HttpRequest TextRequest;
    private HttpRequest ImageRequest;

    // scene reference - find player, background, platform... in the sub-viewport
    private SubViewport SubViewport;
    
    const String WorldNodePath = "PanelContainer/HBoxContainer/VBoxContainer/SubViewportContainer/SubViewport/World";


    // openrouter api for current test
    const String OpenRouterUrl = "https://openrouter.ai/api/v1/chat/completions";
    
    private const String SiteRefer = "https://fusestreet.app";
    private const String TextModel = "google/gemini-3-pro-preview";
    private const String ImageModel = "google/gemini-3-pro-image-preview";
    private const String APIKey = "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";

    // Track if subviewport has focus for input routing
    private bool subViewportFocused = false;
    private SubViewportContainer _subViewportContainer;


    public override void _Ready()
    {
        TextRequest = GetNode<HttpRequest>("TextRequest");
        ImageRequest = GetNode<HttpRequest>("ImageRequest");
        SubViewport = GetNode<SubViewport>("PanelContainer/HBoxContainer/VBoxContainer/SubViewportContainer/SubViewport");
        _subViewportContainer = GetNode<SubViewportContainer>("PanelContainer/HBoxContainer/VBoxContainer/SubViewportContainer");

        PlayButton.Pressed += OnPlayButtonPressed;
        ScreenshotButton.Pressed += OnSnapshotPressed;
        ResetButton.Pressed += OnResetPressed;

        UserDesTextEdit.FocusEntered += () => subViewportFocused = false;
        Culture1LineEdit.FocusEntered += () => subViewportFocused= false;
        Culture2LineEdit.FocusEntered += () => subViewportFocused = false;

        _subViewportContainer.GuiInput += OnSubViewportGuiInput;
    }


    public override void _Process(double delta)
    {
        // Update player input state based on focus
        var player = SubViewport.GetNode<Player>("World/Player");
        player.InputEnabled = subViewportFocused;
    }


    // PlayButton pressed: call task based on Toggle status
    private void OnPlayButtonPressed()
    {
        if (ToggleButton.ButtonPressed)
        {
            if (string.IsNullOrWhiteSpace(Culture2LineEdit.Text.StripEdges()))
            {
                GD.PushWarning("Culture 2 is empty! Switch to generation mode instead...");
                return;
            }
            _ = FusionPressedAsync();  // FUSION MODE
        }
        else
        {
            _ = GeneratePressedAsync();  // GENERATION MODE
        }
    }

    
    private async Task GeneratePressedAsync()
    {
        GD.Print(">> starting generation pipeline... <<");
        PlayButton.Disabled = true;

        try
        {
            // step 1: generate visual description from gemini
            ArtDescriptionResponse artData = await GetArtDirectionFromGemini(
                UserDesTextEdit.Text.StripEdges(),
                Culture1LineEdit.Text.StripEdges()      
            ) ?? throw new Exception("failed to get art direction!");

            GD.Print($"BACKGROUND: {artData.background_prompt}");
            GD.Print($"PLAYER: {artData.player_visual}");
            GD.Print($"PLATFORM: {artData.platform_visual}");

            // step 2: generate player texture
            GD.Print("generating player sprite...");

            Texture2D playerTexture = await GenerateTexture(artData.player_visual, true, false);

            // not apply the texture if generation failed
            if (playerTexture != null) 
            {
                ApplyTextureToPlayer(playerTexture);
            }
            else
            {
                GD.PushError("player texture generation failed. Skipping!");
            }

            // step 3: generate background texture
            GD.Print("generating background...");

            Texture2D backgroundTexture = await GenerateTexture(artData.background_prompt, false, false);
            if (backgroundTexture != null)
            {
                ApplyTextureToBackground(backgroundTexture);
            }

            // step 4: generate platform texture
            GD.Print("generating platform...");

            string platformPrompt = !string.IsNullOrEmpty(artData.platform_visual) 
                                    ? artData.platform_visual 
                                    : "Ground texture matching " + artData.background_prompt; // add a fall-back
            
            Texture2D platformTexture = await GenerateTexture(platformPrompt, false, true);
            if (platformTexture != null)
            {
                ApplyTextureToPlatform(platformTexture);
            }

            GD.Print("current generation pipeline done!");
        }
        catch (Exception e)
        {
            GD.PushError("pipeline ERROR! : " + e.Message);
        }
        finally
        {
            PlayButton.Disabled = false;
        }
    }

    // Text Generation
    private async Task<ArtDescriptionResponse> GetArtDirectionFromGemini(string userDescription, string cultureDefinition)
    {
        // system prompt
        String systemPrompt = @"
        You are the lead tech artist for a 2D side-scrolling platformer game.
        Your job is to convert the user's idea and culture description into three short PROMPTS
        for an image generator: one for the BACKGROUND, one for the PLAYER, and one for the PLATFORM.

        You must always follow these rules:

        1) GLOBAL STYLE (applies to all three prompts)
        - Stylised 2D game art inspired by pixel art / retro CG platformers:
        - sharp, blocky shading, clear outlines, no soft painterly gradients.
        - visible but clean pixel structure or crisp comic-like inking.
        - Everything is in focus: no depth-of-field blur, no soft-focus smearing, no photographic bokeh.
        - Use a consistent art style, colour palette and lighting across BACKGROUND, PLAYER and PLATFORM.

        2) BACKGROUND (""background_prompt"")
        - Goal: the main side-view environment layer behind the ground where the player runs,
        like the street / ruins / forest edge seen in classic 2D platformers.
        - Camera: side view, medium distance; not an extreme close-up wall, not a tiny far-away skyline.
        - Horizontal framing: a wide continuous scene that can scroll horizontally.

        - Vertical composition:
        - Bottom 10-15% of the image:
            - Simple band that continues the colour of the street or floor behind the gameplay platform.
            - Little or no detail here; this area may be partially covered by the platform sprite.
        - From about 15% up to about 60-65% of the image height:
            - The main row of mid-distance environment forms that match the culture:
            - e.g. building facades, doors, windows, arches, balconies, shop fronts, signs, pipes, columns,
                tree trunks, rock faces, ruins, etc., depending on the setting.
            - Scale elements so they look believable relative to a human-sized character in a platformer.
            - The tallest elements must stay clearly below the top of the image so that a sky band remains visible.
        - Top 25-35% of the image:
            - Clear sky or atmospheric gradient appropriate to the scene (or interior ceiling if indoors),
            optionally with a softer distant skyline or silhouettes.
            - No buildings or objects should touch the very top edge of the image.

        - Style:
        - Stylised 2D game art with crisp pixel-art-like blocks or clean inked line art,
            similar sharpness to the player sprite.
        - Medium detail: enough features to recognise the culture and setting, but avoid noisy micro-texture.
        - Do NOT generate a single flat close-up wall/door that fills the frame.
        - Do NOT generate only a far skyline with almost empty mid-ground.
        - DO NOT draw any measurements, percentages, labels, or diagrams.
        - DO NOT include any text, numbers, or UI elements in the background.
        - 不要在背景图中画任何文字、数字、百分比或标注。

        3) PLAYER (""player_visual"")
        - Goal: a 2-row SIDE-VIEW CHARACTER SPRITESHEET for a platformer.
        - Style: pixel-art-inspired or sharp cel-style 2D:
        - clear outline, strong readable silhouette, limited but harmonious colour palette.

        - BACKGROUND (非常重要):
        - Pure white (#FFFFFF) flat background only, identical in every frame.
        - No ground, no shadow, no horizon line, no props, no UI, no text, no other people.
        - The whole image is just the character spritesheet on white.
        - 绝对不要在图片中写任何文字、标签、标题或水印。

        - SPRITESHEET LAYOUT (非常重要):
        - The output is a SINGLE IMAGE with a 2-row grid of character sprites.
        - Row 1 (top row): idle pose, EXACTLY 4 frames, ALL NEARLY IDENTICAL.
            - The character should look almost the same in all 4 idle frames.
            - Only micro-movement like subtle breathing, NOT different poses.
            - 待机帧必须几乎完全相同，不要有不同的姿势。
        - Row 2 (bottom row): running cycle, EXACTLY 6 frames showing run animation.
        - All frames are SIDE VIEW, facing RIGHT, full body visible.
        - Every frame is the same size, same camera distance, same proportions.
        - The character's FEET must sit on the SAME HORIZONTAL BASELINE in every frame.
        - There must be ONLY ONE character per frame.

        - FORBIDDEN (禁止):
        - DO NOT use pure white (#FFFFFF) or very light colours (above #E0E0E0) for ANY part of the character's clothing, skin, hair, or equipment. The white background must remain distinct from the character.
        - 禁止使用纯白色或非常浅的颜色 (高于#E0E0E0) 作为角色的任何服装、皮肤、头发或装备。白色背景必须与角色明显区分。
        - DO NOT write any text like 'idle', 'run', 'frame 1', etc.
        - DO NOT add titles, labels, watermarks, or UI elements.
        - DO NOT show multiple characters in a single frame.
        - DO NOT show different angles (only side view facing right).
        - 不要写任何文字、标题、标签或水印。
        - 不要在一个格子里画多个人物。

        4) PLATFORM (""platform_visual"")
        - Goal: a ground material texture the player runs on.
        - A single seamless square tile viewed from straight top-down (orthographic).
        - The surface must look like walkable ground: dirt, sand, stone, grass, or an equivalent material
        that fits the culture.
        - Style: pixel-art-like or sharp CG:
        - clear but simple tonal blocks, not noisy realism.
        - Very low contrast, soft mottled detail; the ground should not draw more attention than the player.
        - Avoid: bricks, checkerboards, mosaics, decorative borders, or anything that looks like a wall or carpet.
        - Useful phrases to include: ""seamless texture"", ""ground tile"", ""top-down view"",
        ""flat lighting"", ""soft irregular mottled surface"", ""subtle variation"", ""walkable ground"".

        OUTPUT FORMAT:
        Return ONLY a single valid JSON object. Do not add explanations, comments, or markdown fences.
        The JSON must have exactly this structure:

        {
        ""background_prompt"": ""..."",
        ""player_visual"": ""..."",
        ""platform_visual"": ""...""
        }
        ";

        // user prompt
        String userPrompt =
        "User idea: " + userDescription +
        ". Culture or setting: " + cultureDefinition +
        ". Use the structure of classic 2D platformer scenes: a side-view environment band behind a ground line," +
        " with mid-distance facades or landscape in the middle of the image and a clear sky band above." +
        " The style should be sharp pixel-art-inspired 2D game art, with no blur." +
        " The player output is a small side-view spritesheet on pure white, and the platform is a top-down seamless ground tile." +
        " Follow the system rules exactly and respond with JSON only.";


        var requestBody = new OpenRouterRequest
        {
            model = TextModel,
            messages =
            [
                new OpenRouterMessage { role = "system", content = systemPrompt },
                new OpenRouterMessage { role = "user",   content = userPrompt   }
            ]
        };

        String jsonResponse = await PostRequest(OpenRouterUrl, JsonSerializer.Serialize(requestBody));

        using (JsonDocument doc = JsonDocument.Parse(jsonResponse))
        {
            string contentString = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            
            
            // handle possible null content from JSON
            if (string.IsNullOrWhiteSpace(contentString))
            {
                GD.PushError("Text generation: 'content' field was null or empty.");
                return null;
            }

            // strip possible ```json fences just in case
            contentString = contentString
                .Replace("```json", string.Empty)
                .Replace("```", string.Empty)
                .Trim();

            return JsonSerializer.Deserialize<ArtDescriptionResponse>(contentString);
        }
    }


    // Fusion Pressed Async
    private async Task FusionPressedAsync()
    {
        GD.Print(">> Starting Fusion Pipeline <<");
        PlayButton.Disabled = true;

        try
        {
            ArtDescriptionResponse artData = await 
            GetFusionArtDirectionFromGemini(
            UserDesTextEdit.Text.StripEdges(),
            Culture1LineEdit.Text.StripEdges(),
            Culture2LineEdit.Text.StripEdges()
            ) ?? throw new Exception("failed to get fusion art direction!");

            GD.Print($"FUSED BACKGROUND: {artData.background_prompt}");
            GD.Print($"FUSED PLAYER: {artData.player_visual}");
            GD.Print($"FUSED PLATFORM: {artData.platform_visual}");

            // Same texture generation flow as regular generation
            GD.Print("generating fused player sprite...");
            Texture2D playerTexture = await GenerateTexture(artData.player_visual, true, false);
            if (playerTexture != null) ApplyTextureToPlayer(playerTexture);
            else GD.PushError("Fused player texture generation failed. Skipping!");
            

            GD.Print("generating fused background...");
            Texture2D backgroundTexture = await GenerateTexture(artData.background_prompt, false, false);

            if (backgroundTexture != null) ApplyTextureToBackground(backgroundTexture);

            GD.Print("generating fused platform...");
            string platformPrompt = !string.IsNullOrEmpty(artData.platform_visual) 
                ? artData.platform_visual 
                : "Ground texture blending " + Culture1LineEdit.Text + " and " + Culture2LineEdit.Text;
            
            Texture2D platformTexture = await GenerateTexture(platformPrompt, false, true);
            if (platformTexture != null) ApplyTextureToPlatform(platformTexture);

            GD.Print("FUSION pipeline done!");
        }
        catch (Exception e)
        {
            GD.PushError("FUSION pipeline ERROR: " + e.Message);
        }
        finally
        {
            PlayButton.Disabled = false;
        }
    }


    private async Task<ArtDescriptionResponse> GetFusionArtDirectionFromGemini(
        String userDescription, String culture1, String culture2)
        {
            // System prompt for fusion
            String systemPrompt = @"
            You are the lead tech artist for a 2D side-scrolling platformer game.
            Your job is to FUSE TWO CULTURES into a HYBRID visual style, then convert the user's idea 
            into three short PROMPTS for an image generator: one for the BACKGROUND, one for the PLAYER, and one for the PLATFORM.

            FUSION PHILOSOPHY:
            - You receive TWO distinct cultures. Your task is to BLEND them into a single cohesive aesthetic.
            - Do NOT simply place elements side-by-side. INTEGRATE them creatively:
            - Mix architectural forms (e.g., Japanese curved roofs on Moroccan riads).
            - Blend costume elements (e.g., Viking furs with Chinese silk patterns).
            - Fuse material textures (e.g., tatami weave with Persian geometric motifs).
            - Harmonise colour palettes from both cultures into one unified scheme.
            - The result should feel like a NEW hybrid culture, as if these two civilisations had merged centuries ago.

            You must always follow these rules:

            1) GLOBAL STYLE (applies to all three prompts)
            - Stylised 2D game art inspired by pixel art / retro CG platformers:
            - sharp, blocky shading, clear outlines, no soft painterly gradients.
            - visible but clean pixel structure or crisp comic-like inking.
            - Everything is in focus: no depth-of-field blur, no soft-focus smearing, no photographic bokeh.
            - Use a consistent FUSED art style, colour palette and lighting across BACKGROUND, PLAYER and PLATFORM.
            - The fusion must be visible in ALL three outputs, not just one.

            2) BACKGROUND (""background_prompt"")
            - Goal: the main side-view environment layer FUSING architectural elements from BOTH cultures.
            - Examples of fusion:
            - Japanese torii gates with Moroccan zellige tile patterns.
            - Viking longhouses with Chinese upturned roof eaves.
            - Egyptian columns with Celtic knot carvings.
            - Mayan pyramids with Greek marble facades.
            - Camera: side view, medium distance; not an extreme close-up wall, not a tiny far-away skyline.
            - Horizontal framing: a wide continuous scene that can scroll horizontally.

            - Vertical composition:
            - Bottom 10-15% of the image:
                - Simple band that continues the colour of the street or floor behind the gameplay platform.
                - Little or no detail here; this area may be partially covered by the platform sprite.
            - From about 15% up to about 60-65% of the image height:
                - The main row of mid-distance environment forms BLENDING both cultures:
                - e.g., building facades mixing both architectural traditions, hybrid doorways, fused window styles,
                combined decorative motifs, blended signage styles, merged structural elements.
                - Scale elements so they look believable relative to a human-sized character in a platformer.
                - The tallest elements must stay clearly below the top of the image so that a sky band remains visible.
            - Top 25-35% of the image:
                - Clear sky or atmospheric gradient appropriate to the fused scene,
                optionally with a softer distant skyline or silhouettes blending both cultures.
                - No buildings or objects should touch the very top edge of the image.

            - Style:
            - Stylised 2D game art with crisp pixel-art-like blocks or clean inked line art,
                similar sharpness to the player sprite.
            - Medium detail: enough features to recognise BOTH cultures in the fusion, but avoid noisy micro-texture.
            - Do NOT generate a single flat close-up wall/door that fills the frame.
            - Do NOT generate only a far skyline with almost empty mid-ground.
            - Do NOT simply split the image with Culture 1 on left and Culture 2 on right.
            - DO NOT draw any measurements, percentages, labels, or diagrams.
            - DO NOT include any text, numbers, or UI elements in the background.
            - 不要在背景图中画任何文字、数字、百分比或标注。

            3) PLAYER (""player_visual"")
            - Goal: a 2-row SIDE-VIEW CHARACTER SPRITESHEET for a platformer.
            - Style: pixel-art-inspired or sharp cel-style 2D:
            - clear outline, strong readable silhouette, limited but harmonious colour palette.

            - BACKGROUND (非常重要):
            - Pure white (#FFFFFF) flat background only, identical in every frame.
            - No ground, no shadow, no horizon line, no props, no UI, no text, no other people.
            - The whole image is just the character spritesheet on white.
            - 绝对不要在图片中写任何文字、标签、标题或水印。

            - SPRITESHEET LAYOUT (非常重要):
            - The output is a SINGLE IMAGE with a 2-row grid of character sprites.
            - Row 1 (top row): idle pose, EXACTLY 4 frames, ALL NEARLY IDENTICAL.
                - The character should look almost the same in all 4 idle frames.
                - Only micro-movement like subtle breathing, NOT different poses.
                - 待机帧必须几乎完全相同，不要有不同的姿势。
            - Row 2 (bottom row): running cycle, EXACTLY 6 frames showing run animation.
            - All frames are SIDE VIEW, facing RIGHT, full body visible.
            - Every frame is the same size, same camera distance, same proportions.
            - The character's FEET must sit on the SAME HORIZONTAL BASELINE in every frame.
            - There must be ONLY ONE character per frame.

            - FORBIDDEN (禁止):
            - DO NOT use pure white (#FFFFFF) or very light colours (above #E0E0E0) for ANY part of the character's clothing, skin, hair, or equipment. The white background must remain distinct from the character.
            - 禁止使用纯白色或非常浅的颜色 (高于#E0E0E0) 作为角色的任何服装、皮肤、头发或装备。白色背景必须与角色明显区分。
            - DO NOT write any text like 'idle', 'run', 'frame 1', etc.
            - DO NOT add titles, labels, watermarks, or UI elements.
            - DO NOT show multiple characters in a single frame.
            - DO NOT show different angles (only side view facing right).
            - 不要写任何文字、标题、标签或水印。
            - 不要在一个格子里画多个人物。

            4) PLATFORM (""platform_visual"")
            - Goal: a ground material texture FUSING patterns/materials from BOTH cultures.
            - Fusion examples:
            - Tatami weave texture with Persian geometric border motifs blended in.
            - Cobblestone base with Aztec sun symbol patterns subtly embedded.
            - Sandy ground with both Arabic calligraphy swirls and Chinese cloud patterns.
            - Wooden planks with both Norse runes and Japanese wood grain aesthetics.
            - A single seamless square tile viewed from straight top-down (orthographic).
            - The surface must look like walkable ground: dirt, sand, stone, grass, wood, or an equivalent material
            that fits the FUSED culture aesthetic.
            - Style: pixel-art-like or sharp CG:
            - clear but simple tonal blocks, not noisy realism.
            - Very low contrast, soft mottled detail; the ground should not draw more attention than the player.
            - Avoid: overly busy patterns, high-contrast mosaics, decorative borders that look like walls.
            - Useful phrases to include: ""seamless texture"", ""ground tile"", ""top-down view"",
            ""flat lighting"", ""soft irregular mottled surface"", ""subtle variation"", ""walkable ground"",
            ""blended cultural patterns"", ""harmonised fusion aesthetic"".

            OUTPUT FORMAT:
            Return ONLY a single valid JSON object. Do not add explanations, comments, or markdown fences.
            The JSON must have exactly this structure:

            {
            ""background_prompt"": ""..."",
            ""player_visual"": ""..."",
            ""platform_visual"": ""...""
            }
            ";

            // User prompt for fusion
            String userPrompt =
            $"User idea: {userDescription}. " +
            $"Culture 1: {culture1}. " +
            $"Culture 2: {culture2}. " +
            "Create a FUSION that harmoniously blends visual elements from BOTH cultures into a unified hybrid aesthetic. " +
            "The background should mix architectural elements, the player should wear fused costume pieces, " +
            "and the platform should blend ground textures/patterns from both traditions. " +
            "Use the structure of classic 2D platformer scenes: a side-view environment band behind a ground line, " +
            "with mid-distance fused facades or landscape in the middle of the image and a clear sky band above. " +
            "The style should be sharp pixel-art-inspired 2D game art, with no blur. " +
            "The player output is a small side-view spritesheet on pure white, and the platform is a top-down seamless ground tile. " +
            "Follow the system rules exactly and respond with JSON only.";

            var requestBody = new OpenRouterRequest
            {
                model = TextModel,
                messages =
                [
                    new OpenRouterMessage { role = "system", content = systemPrompt },
                    new OpenRouterMessage { role = "user",   content = userPrompt   }
                ]
            };

            String jsonResponse = await PostRequest(OpenRouterUrl, JsonSerializer.Serialize(requestBody));

            if (string.IsNullOrEmpty(jsonResponse)) return null;

            using JsonDocument doc = JsonDocument.Parse(jsonResponse);
            string contentString = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(contentString))
            {
                GD.PushError("Fusion text generation: 'content' field was null or empty.");
                return null;
            }

            contentString = contentString
                .Replace("```json", string.Empty)
                .Replace("```", string.Empty)
                .Trim();

            return JsonSerializer.Deserialize<ArtDescriptionResponse>(contentString);
        }


    // image generation
    private async Task<Texture2D> GenerateTexture(String prompt, bool isCharacter, bool isPlatform)
    {
        if (isCharacter)
        {
            prompt +=
                "\n\n=== STRICT SPRITESHEET REQUIREMENTS ===\n" +
                
                "OUTPUT FORMAT:\n" +
                "- A single image containing a 2-row character spritesheet grid.\n" +
                "- Background MUST be pure solid White (#FFFFFF), no gradients, no patterns.\n" +
                "- NO text, NO labels, NO titles, NO watermarks anywhere.\n" +
                "- NO borders, NO frames, NO boxes around the characters.\n" +
                "- NO rectangular outlines around each sprite.\n" +
                "- DO NOT draw grid lines or cell borders.\n" +
                "- ONLY the character sprites on pure white, nothing else.\n" +
                "- 不要在角色周围画边框、方框或网格线。\n" +
                
                "\nGRID STRUCTURE:\n" +
                "- Row 1 (Top): EXACTLY 4 idle frames, evenly spaced.\n" +
                "- Row 2 (Bottom): EXACTLY 6 run frames, evenly spaced.\n" +
                "- Characters float on white background with NO visible borders.\n" +
                "- The grid is INVISIBLE - only the characters are drawn.\n" +
                
                "\nROW 1 - IDLE (4 frames):\n" +
                "- All 4 frames: EXACT SAME CHARACTER, EXACT SAME POSE.\n" +
                "- Standing still, facing RIGHT, full body visible.\n" +
                "- Make all 4 frames IDENTICAL if unsure.\n" +
                "- 待机帧必须完全相同。\n" +
                
                "\nROW 2 - RUN CYCLE (6 frames):\n" +
                "- Smooth 6-frame running animation loop.\n" +
                "- Character runs facing RIGHT, full body visible.\n" +
                "- Same character design in all frames.\n" +
                
                "\nFORBIDDEN - DO NOT INCLUDE:\n" +
                "- Any text or labels\n" +
                "- Any borders or frames around sprites\n" +
                "- Any grid lines or cell outlines\n" +
                "- Any UI elements or annotations\n" +
                "- Any boxes, rectangles, or decorative frames\n" +
                "- 禁止：文字、边框、网格线、方框、标注";
        }
        
        
        // compute ratio directly
        var ratio = isCharacter ? "16:9" : isPlatform ? "1:1" : "21:9";

        // payload for nano banana
        var reqBody = new
        {
            model = ImageModel,
            messages = new[]
            {
                new {role = "user", content = prompt}
            },
            image_config = new
            {
                aspect_ratio = ratio
            }
        };

        String jsonResponse = await PostRequest(OpenRouterUrl, JsonSerializer.Serialize(reqBody));

        // safety check for request
        if (String.IsNullOrEmpty(jsonResponse))
        {
            GD.PushError("image generation: API returned null.");
            return null;
        }

        // extract URL from JSON
        using (JsonDocument doc = JsonDocument.Parse(jsonResponse))
        {
            // referring to OpenRouter's API documentation for image generation
            var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");

            // CRITICAL CHECK if "images" exist (safety filter causes it to disappear)
            if (message.TryGetProperty("images", out JsonElement imagesArray))
            {
                // according to python reference: images[0] -> image_url -> url
                String imageUrl = imagesArray[0]
                    .GetProperty("image_url")
                    .GetProperty("url")
                    .GetString();
                
                // download image
                return await DownloadImage(imageUrl);
            }
            
            String content = message.GetProperty("content").GetString();
            GD.PushError($"Gemini Refused to generate image. Reason: {content}");
            return null;
            
        }
        
    }


    // helper: SEND HTTP REQUEST
    // New Helper: Creates a fresh HTTP node for every request to prevent "Error 0" locking issues
    private async Task<String> PostRequest(String url, String jsonBody)
    {
        // Create a disposable request node
        HttpRequest request = new HttpRequest();
        AddChild(request);
    
        // Safety: Ensure it cleans up even if it crashes
        try 
        {
            // Headers
            String[] headers = [
                "Content-Type: application/json",
                $"Authorization: Bearer {APIKey}",
                $"HTTP-Referer: {SiteRefer}"
            ];

            // Send
            Error err = request.Request(url, headers, HttpClient.Method.Post, jsonBody);
        
            if (err != Error.Ok)
            {
                GD.PushError($"Godot Connection Error: {err}");
                return null;
            }

            // Wait for response
            var result = await ToSignal(request, HttpRequest.SignalName.RequestCompleted);
        
            long responseCode = (long)result[1];
            byte[] body = (byte[])result[3];

            if (responseCode == 200)
            {
                return System.Text.Encoding.UTF8.GetString(body);
            }
            else
            {
                String errorMsg = System.Text.Encoding.UTF8.GetString(body);
                GD.PushError($"API Error (Code {responseCode}): {errorMsg}");
                return null;
            }
        }
        catch (Exception e)
        {
            GD.PushError($"Request Exception: {e.Message}");
            return null;
        }
        finally
        {
            // ALWAYS clean up the node
            request.QueueFree();
        }
    }


    // modified helper: handle both URLs (https://) and Base64 Data (data:image/...)
    private async Task<Texture2D> DownloadImage(String imageData)
    {
        // CASE 1: AI returned the image directly as Base64 text
        if (imageData.StartsWith("data:image"))
        {
            GD.Print("Image is Base64 encoded. Converting directly...");
            
            // strip the header ("data:image/jpeg;base64,")
            // use char overload to avoid culture-specific warning
            int commaIndex = imageData.IndexOf(',');
            if (commaIndex < 0)
            {
                GD.PushError("Base64 image data did not contain a comma separator.");
                return null;
            }

            string pureBase64 = imageData.Substring(commaIndex + 1);

            byte[] imageBytes = Convert.FromBase64String(pureBase64);
            
            // select the loader based on the header (jpeg or png)
            Image img = new Image();
            Error err;
            
            if (imageData.Contains("image/png"))
            {
                err = img.LoadPngFromBuffer(imageBytes);
            }
            else
            {
                err = img.LoadJpgFromBuffer(imageBytes);
            }

            if (err == Error.Ok)
            {
                return ImageTexture.CreateFromImage(img);
            }
            else 
            {
                GD.PushError("Failed to convert Base64 image to Texture.");
                return null;
            }
        }

        // CASE 2: AI returned a web link (https://...) - Use HTTP Request
        HttpRequest downloader = new HttpRequest();
        AddChild(downloader);

        Error reqErr = downloader.Request(imageData);
        if (reqErr != Error.Ok)
        {
            GD.PushError(string.Concat("Error: Failed to start image downloading. URL was: ", imageData.AsSpan(0, 20), "..."));
            downloader.QueueFree();
            return null;
        }

        var result = await ToSignal(downloader, HttpRequest.SignalName.RequestCompleted);
        downloader.QueueFree();

        long respCode = (long)result[1];
        byte[] body = (byte[])result[3];

        if (respCode == 200)
        {
            Image img = new Image();
            Error loadErr = img.LoadPngFromBuffer(body);
            if (loadErr != Error.Ok)
            {
                loadErr = img.LoadJpgFromBuffer(body);
            }

            if (loadErr == Error.Ok)
            {
                return ImageTexture.CreateFromImage(img);
            }
        }

        GD.PushWarning("Image Download Failed! Code: " + respCode.ToString());
        return null;
    }


    // helper: apply texture to player
    private void ApplyTextureToPlayer(Texture2D PlayerTexture)
    {
        var player = SubViewport.GetNode<Player>("World/Player");
        player.UpdateVisuals(PlayerTexture);
    }

    
    // helper: apply texture to background
    private void ApplyTextureToBackground(Texture2D BackgroundTexture)
    {
        var Background = GetNode<TextureRect>(WorldNodePath + "/StaticBody2DBackgroud/Background");
        Background.Texture = BackgroundTexture;
        Background.StretchMode = TextureRect.StretchModeEnum.Scale;
        Background.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
    }


    // helper: apply texture to platform
    private void ApplyTextureToPlatform(Texture2D PlatformTexture)
    {
        // get the raw image data
        Image img = PlatformTexture.GetImage();

        // resize it for better tiling
        img.Resize(256, 256, Image.Interpolation.Lanczos);
        // create a new smaller texture for tiling
        ImageTexture tilingTexture = ImageTexture.CreateFromImage(img);

        TextureRect Platform = GetNode<TextureRect>(WorldNodePath + "/StaticBody2DPlatform/Platform");
        Platform.Texture = tilingTexture;
        Platform.StretchMode = TextureRect.StretchModeEnum.Tile;
        Platform.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
    }


    private void OnResetPressed()
    {
        GD.Print("Resetting scene to blank state...");
    
        // Reset player to placeholder
        var player = SubViewport.GetNode<Player>("World/Player");
        player.ResetVisuals();  // This method is added in Player.cs
        
        // Reset background to white/blank
        var background = GetNode<TextureRect>(WorldNodePath + "/StaticBody2DBackgroud/Background");
        background.Texture = null;
        
        // Reset platform to white/blank
        var platform = GetNode<TextureRect>(WorldNodePath + "/StaticBody2DPlatform/Platform");
        platform.Texture = null;
        
        GD.Print("Scene reset complete.");
    }


    private void OnSnapshotPressed()
    {
        GD.Print("Taking snapshot...");
        
        // Get the viewport texture
        var viewportTexture = SubViewport.GetTexture();
        var image = viewportTexture.GetImage();
        
        // Create filename with timestamp
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var filename = $"snapshot_{timestamp}.png";
        
        var folderPath = "user://SavedScreenshots";

        if (!DirAccess.DirExistsAbsolute(folderPath))
        {
            var dir = DirAccess.Open("user://");
            if (dir != null)
            {
                var makedirError = dir.MakeDir("SavedScreenshots");
                if (makedirError != Error.Ok)
                {
                    GD.PushError("Fail to create SaveScreenshots folder");
                    return;
                }
                GD.Print("Created SaveScreenshots folder");
            }
            else
            {
                GD.PushError("dir not exists!");
                return;
            }
        }

        // Save to user://SavedScreenshots/
        var savePath = $"{folderPath}/{filename}";
        var error = image.SavePng(savePath);

        if (error == Error.Ok)
        {
            var globalPath = ProjectSettings.GlobalizePath(savePath);
            GD.Print($"Snapshot saved to: {globalPath}");

            OS.ShellOpen(ProjectSettings.GlobalizePath(folderPath)); // Open the folder in system file explorer
        }
        else
        {
            GD.PushError($"Failed to save snapshot: {error}");
        }
    }


    // Helper: Avoid interferring of wasd and space key operation between game runtime screen focus and UI focus
    private void OnSubViewportGuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true })
        {
            subViewportFocused = true;
            // Release focus from any UI element
            UserDesTextEdit.ReleaseFocus();
            Culture1LineEdit.ReleaseFocus();
            Culture2LineEdit.ReleaseFocus();
        }
    }

}

//--------------------------------
// DATA STRUCTURE FOR JSON PARSING
//--------------------------------

// structure sent to openrouter
public class OpenRouterRequest
{
    public String model { get; set; }
    public OpenRouterMessage[] messages { get; set; }
}

public class OpenRouterMessage
{
    public String role { get; set; }
    public String content { get; set; }
}

// structure received from Gemini 3 pro (TEXT JSON DATA)
public class ArtDescriptionResponse
{
    public String background_prompt { get; set; }
    public String player_visual { get; set; }
    public String platform_visual { get; set; }
    
    // add FUSION and NPC visual Later
}

// structure received from Nano Banana pro (IMAGE)
public class NanoBananaResponse
{
    public Choice[] choices { get; set; }
    public class Choice {public Message message { get; set; } }
    public class Message
    {
        public String content { get; set; }
        public ImageURL[] images { get; set; }
    }
    public class ImageURL {public URL url { get; set; }}
    public class URL {public String url { get; set; }}
} */