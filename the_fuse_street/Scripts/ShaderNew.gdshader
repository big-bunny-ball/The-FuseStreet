shader_type canvas_item;

// splified version of removing white background
uniform vec3 key_color : source_color = vec3(1.0, 1.0, 1.0); // C# will update this
uniform float similarity : hint_range(0.0, 1.0) = 0.1;
uniform float smoothness : hint_range(0.0, 1.0) = 0.05;

void fragment()
{
    vec4 tex_color = texture(TEXTURE, UV);

    // calculate difference between pixel and key color
    float diff = distance(tex_color.rgb, key_color);

    // create an alpha mask based on similarity
    float alpha = smoothstep(similarity, similarity + smoothness, diff);

    COLOR = vec4(tex_color.rgb, tex_color.a * alpha);
}




/*
shader_type canvas_item;

// Set this from code to the actual background colour (usually white)
uniform vec3 key_color: source_color = vec3(1.0, 1.0, 1.0);

// How close to key_color we treat as background
uniform float similarity: hint_range(0.0, 1.0) = 0.015;
//uniform float smoothness: hint_range(0.0, 1.0) = 0.02;

// We only remove VERY desaturated pixels near key_color
uniform float max_bg_saturation : hint_range(0.0, 1.0) = 0.06;

void fragment()
{
    vec4 col = texture(TEXTURE, UV);

    // Default: keep the pixel
    float alpha = col.a;

    if (alpha >= 0.01)
    {
        // Colour distance to key_color
        float diff = distance(col.rgb, key_color);

        // Saturation
        float maxc = max(col.r, max(col.g, col.b));
        float minc = min(col.r, min(col.g, col.b));
        float sat  = (maxc - minc) / max(maxc, 0.0001);

        // Only kill pixels which are BOTH:
        // 1) Very close to key_color (almost the same RGB)
        // 2) Very low saturation (almost grey/white)
        if (diff < similarity && sat < max_bg_saturation)
        {
            alpha = 0.0;
        }
    }

    COLOR = vec4(col.rgb, alpha);
}
*/