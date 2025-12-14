shader_type canvas_item;

uniform vec4 key_colour: source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float similarity: hint_range(0.0, 1.0) = 0.15;
uniform float smoothness: hint_range(0.0, 1.0) = 0.08;
uniform float edge_erosion: hint_range(0.0, 1.0) = 0.3;

void fragment()
{
    vec4 tex_colour = texture(TEXTURE, UV);
    vec2 pixel_size = TEXTURE_PIXEL_SIZE;

    // Calculate difference from background colour
    float diff = distance(tex_colour.rgb, key_colour.rgb);

    // Base alpha from chroma key
    float alpha = smoothstep(similarity, similarity + smoothness, diff);

    // EDGE EROSION: Sample neighbours to detect and shrink edges
    float neighbour_bg_count = 0.0;
    vec2 offsets[8] = vec2[](
    vec2(-1.0, 0.0), vec2(1.0, 0.0),
    vec2(0.0, -1.0), vec2(0.0, 1.0),
    vec2(-1.0, -1.0), vec2(1.0, -1.0),
    vec2(-1.0, 1.0), vec2(1.0, 1.0)
    );

    for (int i = 0; i < 8; i++)
    {
        vec2 sample_uv = UV + offsets[i] * pixel_size * 2.0;
        vec4 neighbour = texture(TEXTURE, sample_uv);
        float neighbour_diff = distance(neighbour.rgb, key_colour.rgb);

        // Count how many neighbours are background-ish
        if (neighbour_diff < similarity + 0.1)
        {
            neighbour_bg_count += 1.0;
        }
    }

    // If pixel has background neighbours, it's on an edge - erode it
    float edge_factor = neighbour_bg_count / 8.0;
    alpha *= 1.0 - (edge_factor * edge_erosion);

    // PREMULTIPLIED ALPHA OUTPUT
    COLOR = vec4(tex_colour.rgb * alpha, alpha * tex_colour.a);
}