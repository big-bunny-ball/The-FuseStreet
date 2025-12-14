using System;
using Godot;
using System.Collections.Generic;

namespace TheFuseStreet.Scripts;

public partial class Player : CharacterBody2D
{
    [ExportCategory("Movement Settings")]
    [Export] public float RunSpeed = 500.0f;
    [Export] public float JumpVelocity = -500.0f;
    [Export] public float JumpGravity = 1500.0f;
    [Export] public float FallGravity = 2500.0f;

    [ExportCategory("Visual Import Settings")]
    [Export] public float TargetVisualHeight = 220.0f;

    private AnimatedSprite2D _visualSprite;
    private ColorRect _placeHolder;
    private bool _animationsReady;
    
    private int _jumpCount;
    private const int MaxJumps = 2;

    // Input handler
    public bool InputEnabled { get; set; } = true; 

    
    // _Ready
    public override void _Ready()
    {
        _visualSprite = GetNode<AnimatedSprite2D>("AnimatedSprite2D");
        _placeHolder = GetNode<ColorRect>("ColorRect");
        _animationsReady = false;
    }


    // === ULTIMATE VERSION ===
    public void UpdateVisuals(Texture2D newTextures)
    {
        if (newTextures == null) return;

        var img = newTextures.GetImage();
        
        if (img.GetFormat() != Image.Format.Rgba8) 
            img.Convert(Image.Format.Rgba8);

        var imgW = img.GetWidth();
        var imgH = img.GetHeight();
        var bgColour = img.GetPixel(0, 0);
        
        GD.Print($"[Player] Sprite sheet: {imgW}x{imgH}, bg colour: {bgColour}");
        
        // DEBUG: Save full sprite sheet
        img.SavePng("user://debug_spritesheet.png");
        GD.Print($"[DEBUG] Saved sprite sheet to: {ProjectSettings.GlobalizePath("user://debug_spritesheet.png")}");
        
        if (_visualSprite.Material is ShaderMaterial mat)
        {
            mat.SetShaderParameter("key_colour", bgColour);
            mat.SetShaderParameter("similarity", 0.15f);
            mat.SetShaderParameter("smoothness", 0.08f);
            mat.SetShaderParameter("edge_erosion", 0.4f);
        }

        var rowH = imgH / 2;
        var frames = new SpriteFrames();

        // --- IDLE (Top Row) ---
        frames.AddAnimation("idle");
        frames.SetAnimationLoop("idle", true);
        frames.SetAnimationSpeed("idle", 1f);

        var idleFrame = ExtractBestFrameFromRegions(img, 0, rowH, bgColour);
        
        if (idleFrame != null)
        {
            frames.AddFrame("idle", idleFrame);
            GD.Print("[Player] Idle: frame extracted successfully");

            // DEBUG: Save the extracted frame
            idleFrame.GetImage().SavePng("user://debug_idle_frame.png");
            GD.Print($"[DEBUG] Saved idle frame to: {ProjectSettings.GlobalizePath("user://debug_idle_frame.png")}");
        }
        else
        {
            var fallback = CreateFallbackFrame(img, new Rect2I(0, 0, rowH, rowH), bgColour, rowH);
            frames.AddFrame("idle", fallback);
            GD.Print("[Player] Idle: using fallback frame");
        }

        // --- RUN (Bottom Row) ---
        frames.AddAnimation("run");
        frames.SetAnimationLoop("run", true);
        frames.SetAnimationSpeed("run", 12f);

        var runFrames = ExtractAllFramesFromRegions(img, rowH, rowH, bgColour);
        
        foreach (var tex in runFrames)
        {
            frames.AddFrame("run", tex);
        }
        
        GD.Print($"[Player] Run: {runFrames.Count} frames");

        if (frames.GetFrameCount("run") == 0)
        {
            frames.AddFrame("run", frames.GetFrameTexture("idle", 0));
        }

        // --- JUMP ---
        frames.AddAnimation("jump");
        frames.SetAnimationLoop("jump", false);
        frames.SetAnimationSpeed("jump", 8f);
        
        var runCount = frames.GetFrameCount("run");
        if (runCount > 2)
        {
            frames.AddFrame("jump", frames.GetFrameTexture("run", 2));
        }
        else
        {
            frames.AddFrame("jump", frames.GetFrameTexture("idle", 0));
        }

        // --- APPLY & SCALE ---
        _visualSprite.SpriteFrames = frames;
        _visualSprite.Play("idle");

        var firstTex = frames.GetFrameTexture("idle", 0);
        var actualHeight = firstTex?.GetHeight() ?? rowH;

        var scaleRatio = TargetVisualHeight / actualHeight;
        _visualSprite.Scale = new Vector2(scaleRatio, scaleRatio);
        _visualSprite.Position = new Vector2(0, -TargetVisualHeight / 2.0f);

        _animationsReady = true;
        _placeHolder.Hide();
    }


    /// <summary>
    /// Finds all individual character regions in a row by detecting vertical gaps.
    /// </summary>
    private static List<(int startX, int endX)> FindCharacterRegions(Image source, int rowY, int rowHeight, Color bgColour)
    {
        int imgW = source.GetWidth();
        float threshold = 0.36f;
        
        // Scan each X position to count how many content pixels it has
        int[] contentCount = new int[imgW];
        int contentStartX = imgW;
        int contentEndX = 0;
        
        for (int x = 0; x < imgW; x++)
        {
            for (int y = rowY; y < rowY + rowHeight; y++)
            {
                var c = source.GetPixel(x, y);
                float diff = Math.Abs(c.R - bgColour.R) + Math.Abs(c.G - bgColour.G) + Math.Abs(c.B - bgColour.B);
                
                if (diff > threshold)
                {
                    contentCount[x]++;
                }
            }
            
            if (contentCount[x] > 0)
            {
                if (x < contentStartX) contentStartX = x;
                if (x > contentEndX) contentEndX = x;
            }
        }
        
        if (contentStartX >= contentEndX)
        {
            GD.Print($"[FindRegions] Row at Y={rowY}: no content found");
            return new List<(int startX, int endX)>();
        }
        
        // Find ALL local minima - even shallow ones
        var gapCandidates = new List<(int x, int score, int depth)>();
        
        int windowSize = 8;
        for (int x = contentStartX + windowSize; x < contentEndX - windowSize; x++)
        {
            int leftSum = 0, rightSum = 0;
            for (int i = 1; i <= windowSize; i++)
            {
                leftSum += contentCount[x - i];
                rightSum += contentCount[x + i];
            }
            
            int centerCount = contentCount[x];
            int avgSide = (leftSum + rightSum) / (windowSize * 2);
            
            // More lenient: accept gaps where center is less than 50% of sides
            if (centerCount < avgSide * 0.5f && avgSide > 5)
            {
                int gapScore = avgSide - centerCount;
                gapCandidates.Add((x, gapScore, centerCount));
            }
        }
        
        // Merge nearby gap candidates and pick strongest
        var gaps = new List<int>();
        int minGapDistance = 40;
        
        gapCandidates.Sort((a, b) => b.score.CompareTo(a.score));
        
        foreach (var (x, score, depth) in gapCandidates)
        {
            bool tooClose = false;
            foreach (int existingGap in gaps)
            {
                if (Math.Abs(x - existingGap) < minGapDistance)
                {
                    tooClose = true;
                    break;
                }
            }
            
            if (!tooClose)
            {
                gaps.Add(x);
            }
        }
        
        gaps.Sort();
        
        GD.Print($"[FindRegions] Row at Y={rowY}: found {gaps.Count} gaps at positions: {string.Join(", ", gaps)}");
        
        // Build regions from gaps
        var regions = new List<(int startX, int endX)>();
        
        if (gaps.Count == 0)
        {
            regions.Add((contentStartX, contentEndX));
        }
        else
        {
            regions.Add((contentStartX, gaps[0]));
            
            for (int i = 0; i < gaps.Count - 1; i++)
            {
                regions.Add((gaps[i], gaps[i + 1]));
            }
            
            regions.Add((gaps[gaps.Count - 1], contentEndX));
        }
        
        // POST-PROCESS: Split any region that's too wide (likely multiple characters)
        int maxCharWidth = (int)(rowHeight * 1.2f);  // A character shouldn't be wider than 1.2x its height
        var finalRegions = new List<(int startX, int endX)>();
        
        foreach (var (startX, endX) in regions)
        {
            int regionWidth = endX - startX;
            
            if (regionWidth > maxCharWidth)
            {
                // This region is too wide - find local minima within it to split
                int estimatedChars = (int)Math.Ceiling((float)regionWidth / (rowHeight * 0.6f));
                estimatedChars = Math.Clamp(estimatedChars, 2, 8);
                
                GD.Print($"[FindRegions] Region {startX}-{endX} too wide ({regionWidth}px), splitting into {estimatedChars} parts");
                
                // Find the best split points within this region
                var localMinima = new List<(int x, int value)>();
                
                for (int x = startX + 20; x < endX - 20; x++)
                {
                    // Check if this is a local minimum
                    bool isMinimum = true;
                    for (int dx = -5; dx <= 5; dx++)
                    {
                        if (dx != 0 && contentCount[x + dx] < contentCount[x])
                        {
                            isMinimum = false;
                            break;
                        }
                    }
                    
                    if (isMinimum && contentCount[x] < rowHeight * 0.8f)
                    {
                        localMinima.Add((x, contentCount[x]));
                    }
                }
                
                // Sort by value (lowest content count = best split point)
                localMinima.Sort((a, b) => a.value.CompareTo(b.value));
                
                // Pick the best split points
                var splitPoints = new List<int>();
                int minSplitDistance = regionWidth / (estimatedChars + 1);
                
                foreach (var (x, value) in localMinima)
                {
                    if (splitPoints.Count >= estimatedChars - 1) break;
                    
                    bool tooClose = false;
                    foreach (int sp in splitPoints)
                    {
                        if (Math.Abs(x - sp) < minSplitDistance)
                        {
                            tooClose = true;
                            break;
                        }
                    }
                    
                    // Also check distance from region edges
                    if (x - startX < minSplitDistance / 2 || endX - x < minSplitDistance / 2)
                    {
                        tooClose = true;
                    }
                    
                    if (!tooClose)
                    {
                        splitPoints.Add(x);
                    }
                }
                
                splitPoints.Sort();
                
                // If we couldn't find good split points, divide evenly
                if (splitPoints.Count < estimatedChars - 1)
                {
                    splitPoints.Clear();
                    int charWidth = regionWidth / estimatedChars;
                    for (int i = 1; i < estimatedChars; i++)
                    {
                        splitPoints.Add(startX + i * charWidth);
                    }
                }
                
                // Create sub-regions
                int prevX = startX;
                foreach (int sp in splitPoints)
                {
                    finalRegions.Add((prevX, sp));
                    prevX = sp;
                }
                finalRegions.Add((prevX, endX));
            }
            else
            {
                finalRegions.Add((startX, endX));
            }
        }
        
        GD.Print($"[FindRegions] Row at Y={rowY}: returning {finalRegions.Count} character regions");
        return finalRegions;
    }


    /// <summary>
    /// Extracts the best single frame from detected character regions.
    /// Used for idle animation.
    /// </summary>
    private static ImageTexture ExtractBestFrameFromRegions(Image source, int rowY, int rowHeight, Color bgColour)
    {
        var regions = FindCharacterRegions(source, rowY, rowHeight, bgColour);
        
        if (regions.Count == 0) return null;
        
        ImageTexture bestFrame = null;
        int bestScore = 0;
        
        foreach (var (startX, endX) in regions)
        {
            var contentRect = GetContentRectStrict(source, startX, endX, rowY, rowY + rowHeight, bgColour);
            
            if (contentRect.Size.X <= 0 || contentRect.Size.Y <= 0) continue;
            
            // Score by size
            int score = contentRect.Size.X * contentRect.Size.Y;
            
            // Reject bad aspect ratios (too wide = likely multiple characters)
            float aspectRatio = (float)contentRect.Size.X / contentRect.Size.Y;
            if (aspectRatio < 0.2f || aspectRatio > 1.5f)
            {
                GD.Print($"[ExtractRegion] Region {startX}-{endX}: rejected, bad aspect ratio {aspectRatio:F2}");
                continue;
            }
            
            // Reject too short
            if (contentRect.Size.Y < rowHeight * 0.3f)
            {
                GD.Print($"[ExtractRegion] Region {startX}-{endX}: rejected, too short");
                continue;
            }
            
            GD.Print($"[ExtractRegion] Region {startX}-{endX}: {contentRect.Size.X}x{contentRect.Size.Y}, score={score}");
            
            if (score > bestScore)
            {
                bestScore = score;
                bestFrame = ExtractFrameToTexture(source, contentRect, bgColour, rowHeight);
            }
        }
        
        return bestFrame;
    }


    /// <summary>
    /// Extracts all unique frames from detected character regions.
    /// Used for run animation.
    /// </summary>
    private static List<ImageTexture> ExtractAllFramesFromRegions(Image source, int rowY, int rowHeight, Color bgColour)
    {
        var regions = FindCharacterRegions(source, rowY, rowHeight, bgColour);
        
        var validFrames = new List<ImageTexture>();
        var frameImages = new List<Image>();
        
        foreach (var (startX, endX) in regions)
        {
            var contentRect = GetContentRectStrict(source, startX, endX, rowY, rowY + rowHeight, bgColour);
            
            if (contentRect.Size.X <= 0 || contentRect.Size.Y <= 0) continue;
            
            float aspectRatio = (float)contentRect.Size.X / contentRect.Size.Y;
            if (aspectRatio < 0.15f || aspectRatio > 3.0f) continue;
            if (contentRect.Size.Y < rowHeight * 0.25f) continue;
            
            var frameImg = ExtractFrameToImage(source, contentRect, bgColour, rowHeight);
            
            // Dedup check
            bool isDuplicate = false;
            foreach (var existingImg in frameImages)
            {
                if (CompareImages(frameImg, existingImg, bgColour) > 0.92f)
                {
                    isDuplicate = true;
                    GD.Print($"[ExtractRegion] Region {startX}-{endX}: duplicate, skipping");
                    break;
                }
            }
            
            if (!isDuplicate)
            {
                frameImages.Add(frameImg);
                validFrames.Add(ImageTexture.CreateFromImage(frameImg));
                GD.Print($"[ExtractRegion] Region {startX}-{endX}: added frame {contentRect.Size.X}x{contentRect.Size.Y}");
            }
        }
        
        return validFrames;
    }


    /// <summary>
    /// Finds content bounds STRICTLY within the given X and Y boundaries.
    /// </summary>
    private static Rect2I GetContentRectStrict(Image img, int xStart, int xEnd, int yStart, int yEnd, Color bgColour)
    {
        int minX = xEnd;
        int maxX = xStart;
        int minY = yEnd;
        int maxY = yStart;
        
        bool hasContent = false;
        float threshold = 0.36f;
        
        for (int x = xStart; x < xEnd; x++)
        {
            for (int y = yStart; y < yEnd; y++)
            {
                var c = img.GetPixel(x, y);
                float diff = Math.Abs(c.R - bgColour.R) + Math.Abs(c.G - bgColour.G) + Math.Abs(c.B - bgColour.B);
                
                if (diff > threshold)
                {
                    hasContent = true;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }
        
        if (!hasContent) return new Rect2I(0, 0, 0, 0);
        
        return new Rect2I(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }


    /// <summary>
    /// Extracts a frame and returns as Image (for comparison).
    /// </summary>
    private static Image ExtractFrameToImage(Image source, Rect2I contentRect, Color bgColour, int canvasHeight)
    {
        int padding = 4;
        int canvasW = contentRect.Size.X + padding * 2;
        int canvasH = Math.Max(contentRect.Size.Y + padding * 2, canvasHeight);
        
        var canvas = Image.CreateEmpty(canvasW, canvasH, false, Image.Format.Rgba8);
        canvas.Fill(new Color(bgColour.R, bgColour.G, bgColour.B, 0));
        
        // Centre horizontally, align to bottom
        int destX = (canvasW - contentRect.Size.X) / 2;
        int destY = canvasH - contentRect.Size.Y - padding;
        
        canvas.BlitRect(source, contentRect, new Vector2I(destX, destY));
        
        return canvas;
    }


    /// <summary>
    /// Extracts a frame and returns as ImageTexture.
    /// </summary>
    private static ImageTexture ExtractFrameToTexture(Image source, Rect2I contentRect, Color bgColour, int canvasHeight)
    {
        var img = ExtractFrameToImage(source, contentRect, bgColour, canvasHeight);
        return ImageTexture.CreateFromImage(img);
    }


    /// <summary>
    /// Compares two images and returns a similarity score (0.0 to 1.0).
    /// </summary>
    private static float CompareImages(Image img1, Image img2, Color bgColour)
    {
        int w1 = img1.GetWidth();
        int h1 = img1.GetHeight();
        int w2 = img2.GetWidth();
        int h2 = img2.GetHeight();
        
        if (Math.Abs(w1 - w2) > 5 || Math.Abs(h1 - h2) > 5)
        {
            return 0.0f;
        }
        
        int w = Math.Min(w1, w2);
        int h = Math.Min(h1, h2);
        
        int sampleStep = Math.Max(1, Math.Min(w, h) / 32);
        
        int totalSamples = 0;
        int matchingSamples = 0;
        
        for (int y = 0; y < h; y += sampleStep)
        {
            for (int x = 0; x < w; x += sampleStep)
            {
                var c1 = img1.GetPixel(x, y);
                var c2 = img2.GetPixel(x, y);
                
                float bg1Diff = Math.Abs(c1.R - bgColour.R) + Math.Abs(c1.G - bgColour.G) + Math.Abs(c1.B - bgColour.B);
                float bg2Diff = Math.Abs(c2.R - bgColour.R) + Math.Abs(c2.G - bgColour.G) + Math.Abs(c2.B - bgColour.B);
                
                bool isBg1 = bg1Diff < 0.15f;
                bool isBg2 = bg2Diff < 0.15f;
                
                if (isBg1 && isBg2)
                {
                    matchingSamples++;
                }
                else if (!isBg1 && !isBg2)
                {
                    float pixelDiff = Math.Abs(c1.R - c2.R) + Math.Abs(c1.G - c2.G) + Math.Abs(c1.B - c2.B);
                    if (pixelDiff < 0.15f)
                    {
                        matchingSamples++;
                    }
                }
                
                totalSamples++;
            }
        }
        
        if (totalSamples == 0) return 0.0f;
        
        return (float)matchingSamples / totalSamples;
    }


    /// <summary>
    /// Simple fallback frame creator.
    /// </summary>
    private static ImageTexture CreateFallbackFrame(Image source, Rect2I rect, Color bgColour, int size)
    {
        var canvas = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        canvas.Fill(bgColour);
        
        var safeRect = new Rect2I(
            Math.Clamp(rect.Position.X, 0, source.GetWidth() - 1),
            Math.Clamp(rect.Position.Y, 0, source.GetHeight() - 1),
            Math.Min(rect.Size.X, source.GetWidth()),
            Math.Min(rect.Size.Y, source.GetHeight())
        );
        
        canvas.BlitRect(source, safeRect, Vector2I.Zero);
        return ImageTexture.CreateFromImage(canvas);
    }


    // ============================================================
    // END OF REPLACEMENT - ResetVisuals() starts after this
    // ============================================================


    // Helper: Reset Visuals (when reset button pressed in UI)
    public void ResetVisuals()
    {
        _animationsReady = false;
        _visualSprite.SpriteFrames = null;
        _visualSprite.Scale = Vector2.One;
        _visualSprite.Position = Vector2.Zero;
        _placeHolder.Show();
    }

    
    public override void _PhysicsProcess(double delta)
    {
        var velocity = Velocity;

        if (!IsOnFloor())
        {
            if (velocity.Y > 0) velocity.Y += FallGravity * (float)delta;
            else velocity.Y += JumpGravity * (float)delta;
        }
        else
        {
            _jumpCount = 0;
        }

        // Only process input if enabled
        if (InputEnabled)
        {
            if (Input.IsActionJustPressed("game_jump"))
            {
                if (IsOnFloor() || _jumpCount < MaxJumps)
                {
                    velocity.Y = JumpVelocity;
                    _jumpCount++;
                }
            }

            var direction = Input.GetAxis("game_left", "game_right");
            if (direction != 0)
            {
                velocity.X = direction * RunSpeed;
                if (_animationsReady) _visualSprite.FlipH = direction < 0;
            }
            else
            {
                velocity.X = Mathf.MoveToward(Velocity.X, 0, RunSpeed);
            }
        }
        else
        {
            // No input - just decelerate
            velocity.X = Mathf.MoveToward(Velocity.X, 0, RunSpeed);
        }

        Velocity = velocity;
        MoveAndSlide();

        if (_animationsReady)
        {
            if (!IsOnFloor()) _visualSprite.Play("jump");
            else if (Math.Abs(Velocity.X) > 10) _visualSprite.Play("run");
            else _visualSprite.Play("idle");
        }
    }
}





/// RUBBISH CODE BACKUP ///

    /* private static ImageTexture[] CreateStabilisedAnimation(
    Image source, 
    int rowY, 
    int rowHeight, 
    int columnCount, 
    Color bgColour,
    bool useStrictAnchor = false)
    {
        var imgW = source.GetWidth();
        var colW = imgW / columnCount;
        
        // === PASS 1: Scan ALL frames to find the GLOBAL bounding box ===
        var frameRects = new Rect2I[columnCount];
        
        int maxContentWidth = 0;
        int maxContentHeight = 0;
        
        for (int i = 0; i < columnCount; i++)
        {
            var cellRect = new Rect2I(i * colW, rowY, colW, rowHeight);
            var contentRect = GetContentRectInBlock(source, cellRect, bgColour);
            frameRects[i] = contentRect;

            if (contentRect.Size.X <= 0 || contentRect.Size.Y <= 0) continue;
            maxContentWidth = Math.Max(maxContentWidth, contentRect.Size.X);
            maxContentHeight = Math.Max(maxContentHeight, contentRect.Size.Y);
        }
        
        // === PASS 1.5: Recalculate max dimensions using CLIPPED bounds ===
        maxContentWidth = 0;
        maxContentHeight = 0;

        for (int i = 0; i < columnCount; i++)
        {
            var contentRect = frameRects[i];
            if (contentRect.Size.X == 0 || contentRect.Size.Y == 0) continue;

            var cellRect = new Rect2I(i * colW, rowY, colW, rowHeight);

            int clippedWidth = Math.Min(contentRect.End.X, cellRect.End.X) - Math.Max(contentRect.Position.X, cellRect.Position.X);
            int clippedHeight = Math.Min(contentRect.End.Y, cellRect.End.Y) - Math.Max(contentRect.Position.Y, cellRect.Position.Y);

            if (clippedWidth > 0 && clippedHeight > 0)
            {
                maxContentWidth = Math.Max(maxContentWidth, clippedWidth);
                maxContentHeight = Math.Max(maxContentHeight, clippedHeight);
            }
        }

        // Early exit if nothing valid found
        if (maxContentWidth == 0 || maxContentHeight == 0)
        {
            return new ImageTexture[columnCount];
        }
        
        // Calculate required canvas size to fit all frames
        int canvasPadding = 8;
        int canvasW = maxContentWidth + canvasPadding;
        int canvasH = Math.Max(maxContentHeight + canvasPadding, rowHeight);
        
        
        // === PASS 2: Create each frame with CONSISTENT positioning ===
        var textures = new ImageTexture[columnCount];

        // === FOR STRICT MODE: Calculate anchor from first valid frame only ===
        int referenceAnchorX = -1;

        if (useStrictAnchor)
        {
            for (int i = 0; i < columnCount; i++)
            {
                var contentRect = frameRects[i];
                if (contentRect.Size.X == 0 || contentRect.Size.Y == 0) continue;
                
                var cellRect = new Rect2I(i * colW, rowY, colW, rowHeight);
                var clippedRect = new Rect2I(
                    Math.Max(contentRect.Position.X, cellRect.Position.X),
                    Math.Max(contentRect.Position.Y, cellRect.Position.Y),
                    Math.Min(contentRect.End.X, cellRect.End.X) - Math.Max(contentRect.Position.X, cellRect.Position.X),
                    Math.Min(contentRect.End.Y, cellRect.End.Y) - Math.Max(contentRect.Position.Y, cellRect.Position.Y)
                );
                
                if (clippedRect.Size.X <= 0 || clippedRect.Size.Y <= 0) continue;
                
                // Check artefact filter first
                var widthRatio = (float)clippedRect.Size.X / maxContentWidth;
                var heightRatio = (float)clippedRect.Size.Y / maxContentHeight;
                var aspectRatio = (float)clippedRect.Size.X / clippedRect.Size.Y;
                
                if (widthRatio < 0.4f || heightRatio < 0.4f) continue;
                if (aspectRatio is < 0.15f or > 4.0f) continue;
                
                // Calculate foot centre for this reference frame
                int footSampleHeight = Math.Max(4, clippedRect.Size.Y / 5);
                int footRegionTop = clippedRect.Position.Y + clippedRect.Size.Y - footSampleHeight;
                
                int footMinX = clippedRect.End.X;
                int footMaxX = clippedRect.Position.X;
                
                for (int x = clippedRect.Position.X; x < clippedRect.End.X; x++)
                {
                    for (int y = footRegionTop; y < clippedRect.End.Y; y++)
                    {
                        var c = source.GetPixel(x, y);
                        float diff = Math.Abs(c.R - bgColour.R) + Math.Abs(c.G - bgColour.G) + Math.Abs(c.B - bgColour.B);
                        if (diff > 0.36f)
                        {
                            if (x < footMinX) footMinX = x;
                            if (x > footMaxX) footMaxX = x;
                        }
                    }
                }
                
                if (footMaxX > footMinX)
                {
                    int footCentre = (footMinX + footMaxX) / 2;
                    referenceAnchorX = footCentre - clippedRect.Position.X;
                }
                else
                {
                    referenceAnchorX = clippedRect.Size.X / 2;
                }
                
                GD.Print($"[Stabiliser] Strict mode: reference anchor set from frame {i}: {referenceAnchorX}px from content left");
                break;
            }
        }

        for (var i = 0; i < columnCount; i++)
        {
            var contentRect = frameRects[i];

            if (contentRect.Size.X == 0 || contentRect.Size.Y == 0)
            {
                textures[i] = null;
                continue;
            }

            // Clip contentRect to cell boundaries
            var cellRect = new Rect2I(i * colW, rowY, colW, rowHeight);

            var clippedRect = new Rect2I(
                Math.Max(contentRect.Position.X, cellRect.Position.X),
                Math.Max(contentRect.Position.Y, cellRect.Position.Y),
                Math.Min(contentRect.End.X, cellRect.End.X) - Math.Max(contentRect.Position.X, cellRect.Position.X),
                Math.Min(contentRect.End.Y, cellRect.End.Y) - Math.Max(contentRect.Position.Y, cellRect.Position.Y)
            );

            // Skip if clipping eliminated the content
            if (clippedRect.Size.X <= 0 || clippedRect.Size.Y <= 0)
            {
                textures[i] = null;
                continue;
            }
            
            // === ARTEFACT FILTER: Reject frames that are too small or wrong shape ===
            var widthRatio = (float)clippedRect.Size.X / maxContentWidth;
            var heightRatio = (float)clippedRect.Size.Y / maxContentHeight;
            var aspectRatio = (float)clippedRect.Size.X / clippedRect.Size.Y;

            if (widthRatio < 0.4f || heightRatio < 0.4f)
            {
                GD.Print($"[Stabiliser] Frame {i} rejected: too small ({widthRatio:P0} width, {heightRatio:P0} height)");
                textures[i] = null;
                continue;
            }

            if (aspectRatio is < 0.15f or > 4.0f)
            {
                GD.Print($"[Stabiliser] Frame {i} rejected: bad aspect ratio ({aspectRatio:F2})");
                textures[i] = null;
                continue;
            }

            // Create canvas
            var canvas = Image.CreateEmpty(canvasW, canvasH, false, Image.Format.Rgba8);
            canvas.Fill(new Color(bgColour.R, bgColour.G, bgColour.B, 0));

            // === CALCULATE ANCHOR POSITION ===
            int canvasCentreX = canvasW / 2;
            int clippedDestX;

            if (useStrictAnchor && referenceAnchorX >= 0)
            {
                // STRICT MODE: Use the reference anchor for ALL frames
                clippedDestX = canvasCentreX - referenceAnchorX;
            }
            else
            {
                // NORMAL MODE: Calculate foot-centre per frame
                int footSampleHeight = Math.Max(4, clippedRect.Size.Y / 5);
                int footRegionTop = clippedRect.Position.Y + clippedRect.Size.Y - footSampleHeight;

                int footMinX = clippedRect.End.X;
                int footMaxX = clippedRect.Position.X;

                for (int x = clippedRect.Position.X; x < clippedRect.End.X; x++)
                {
                    for (int y = footRegionTop; y < clippedRect.End.Y; y++)
                    {
                        var c = source.GetPixel(x, y);
                        float diff = Math.Abs(c.R - bgColour.R) + Math.Abs(c.G - bgColour.G) + Math.Abs(c.B - bgColour.B);
                        
                        if (diff > 0.36f)
                        {
                            if (x < footMinX) footMinX = x;
                            if (x > footMaxX) footMaxX = x;
                        }
                    }
                }

                int anchorX;
                if (footMaxX > footMinX)
                {
                    anchorX = (footMinX + footMaxX) / 2;
                }
                else
                {
                    anchorX = clippedRect.Position.X + clippedRect.Size.X / 2;
                }

                clippedDestX = canvasCentreX - (anchorX - clippedRect.Position.X);
            }

            int clippedDestY = canvasH - clippedRect.Size.Y - 2;

            // Clamp to canvas bounds
            clippedDestX = Math.Clamp(clippedDestX, 0, Math.Max(0, canvasW - clippedRect.Size.X));
            clippedDestY = Math.Max(0, clippedDestY);

            canvas.BlitRect(source, clippedRect, new Vector2I(clippedDestX, clippedDestY));
            textures[i] = ImageTexture.CreateFromImage(canvas);
        }
        
        return textures;
    } */