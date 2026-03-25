#!/usr/bin/env python3
"""
Désobfuscateur de shaders Unity (HLSL/CG)
Gère les shaders obfusqués avec des macros #define et des tableaux de constantes.
Usage: python deobfuscate_shader.py <input.shader> [output.shader]
"""

import re
import sys
from pathlib import Path
from collections import OrderedDict


def extract_handler_array(content: str) -> dict[int, str]:
    """Extrait les valeurs du tableau handler[] si présent."""
    handler_values = {}
    # Cherche: static float handler[] = {val1, val2, ...};
    pattern = r'static\s+float\s+handler\s*\[\s*\]\s*=\s*\{([^}]+)\}'
    match = re.search(pattern, content)
    if match:
        values_str = match.group(1)
        # Parse les valeurs séparées par des virgules
        values = [v.strip() for v in values_str.split(',')]
        for i, val in enumerate(values):
            handler_values[i] = val
    return handler_values


def extract_defines(content: str) -> OrderedDict[str, str]:
    """Extrait toutes les macros #define."""
    defines = OrderedDict()
    # Pattern pour #define NAME VALUE (sur une ligne)
    pattern = r'#define\s+(\w+)\s+(.+?)(?=\n|$)'
    for match in re.finditer(pattern, content):
        name = match.group(1)
        value = match.group(2).strip()
        defines[name] = value
    return defines


def resolve_handler_refs(defines: dict, handler_values: dict) -> dict:
    """Résout les références handler[index] dans les defines."""
    resolved = {}
    for name, value in defines.items():
        # Cherche handler[N] ou N_xxx
        handler_match = re.match(r'handler\[(\d+)\]', value)
        if handler_match:
            idx = int(handler_match.group(1))
            if idx in handler_values:
                resolved[name] = handler_values[idx]
            else:
                resolved[name] = value
        else:
            resolved[name] = value
    return resolved


def resolve_chained_defines(defines: dict, max_iterations: int = 100) -> dict:
    """Résout les defines chaînés (A -> B -> C -> valeur_réelle)."""
    resolved = dict(defines)
    
    for _ in range(max_iterations):
        changed = False
        for name, value in list(resolved.items()):
            # Si la valeur est elle-même un identifiant défini
            if value in resolved and value != name:
                resolved[name] = resolved[value]
                changed = True
        if not changed:
            break
    
    return resolved


def identify_keywords(defines: dict) -> dict:
    """Identifie les mots-clés HLSL/CG standard parmi les valeurs."""
    hlsl_keywords = {
        'float', 'float2', 'float3', 'float4',
        'half', 'half2', 'half3', 'half4',
        'fixed', 'fixed2', 'fixed3', 'fixed4',
        'int', 'int2', 'int3', 'int4',
        'uint', 'uint2', 'uint3', 'uint4',
        'bool', 'bool2', 'bool3', 'bool4',
        'sampler2D', 'sampler3D', 'samplerCUBE',
        'float2x2', 'float3x3', 'float4x4',
        'void', 'return', 'if', 'else', 'for', 'while', 'do',
        'break', 'continue', 'discard',
        'struct', 'const', 'static', 'uniform',
        'in', 'out', 'inout',
        'lerp', 'saturate', 'clamp', 'step', 'smoothstep',
        'sin', 'cos', 'tan', 'asin', 'acos', 'atan', 'atan2',
        'pow', 'exp', 'log', 'log2', 'sqrt', 'rsqrt',
        'abs', 'sign', 'floor', 'ceil', 'frac', 'round',
        'min', 'max', 'fmod', 'mod',
        'dot', 'cross', 'normalize', 'length', 'distance',
        'reflect', 'refract',
        'mul', 'transpose', 'determinant',
        'tex2D', 'tex2Dlod', 'tex2Dproj', 'texCUBE',
        'ddx', 'ddy', 'fwidth',
        'clip', 'all', 'any',
        'POSITION', 'NORMAL', 'TANGENT', 'TEXCOORD0', 'TEXCOORD1',
        'COLOR', 'SV_POSITION', 'SV_Target',
        'UNITY_MATRIX_MVP', 'UNITY_MATRIX_MV', 'UNITY_MATRIX_V', 'UNITY_MATRIX_P',
        '_Time', '_WorldSpaceCameraPos', '_WorldSpaceLightPos0',
    }
    
    keyword_defines = {}
    for name, value in defines.items():
        if value in hlsl_keywords:
            keyword_defines[name] = value
    
    return keyword_defines


def deobfuscate(content: str) -> str:
    """Désobfusque le contenu du shader."""
    
    # 1. Extraire le tableau handler[] si présent
    handler_values = extract_handler_array(content)
    
    # 2. Extraire tous les #define
    defines = extract_defines(content)
    
    # 3. Résoudre les références handler[N]
    if handler_values:
        defines = resolve_handler_refs(defines, handler_values)
    
    # 4. Résoudre les defines chaînés
    resolved = resolve_chained_defines(defines)
    
    # 5. Identifier les mots-clés
    keywords = identify_keywords(resolved)
    
    # 6. Construire la table de remplacement finale
    # On priorise les remplacements du plus long au plus court pour éviter les conflits
    replacements = {}
    for name, value in resolved.items():
        # Ne remplacer que si c'est un identifiant obfusqué (long, mélange de l et I)
        if len(name) > 20 or (re.match(r'^(st|N_|Cu|xh)[lI1i]+', name)):
            replacements[name] = value
    
    # Trier par longueur décroissante
    sorted_replacements = sorted(replacements.items(), key=lambda x: -len(x[0]))
    
    # 7. Appliquer les remplacements
    result = content
    
    # Supprimer les lignes #define obfusquées
    lines = result.split('\n')
    clean_lines = []
    for line in lines:
        # Garder la ligne si ce n'est pas un #define obfusqué
        if line.strip().startswith('#define'):
            match = re.match(r'\s*#define\s+(\w+)', line)
            if match:
                name = match.group(1)
                # Si c'est un define obfusqué, on le retire
                if name in replacements or len(name) > 30:
                    continue
        # Supprimer aussi le tableau handler
        if 'static float handler[]' in line or 'static float handler []' in line:
            continue
        clean_lines.append(line)
    
    result = '\n'.join(clean_lines)
    
    # 8. Remplacer les identifiants obfusqués par leurs valeurs
    for obfu_name, real_value in sorted_replacements:
        # Utiliser des word boundaries pour éviter les remplacements partiels
        pattern = r'\b' + re.escape(obfu_name) + r'\b'
        result = re.sub(pattern, real_value, result)
    
    # 9. Nettoyer les lignes vides multiples
    result = re.sub(r'\n{3,}', '\n\n', result)
    
    # 10. Reformater le code (indentation basique)
    result = format_code(result)
    
    return result


def format_code(content: str) -> str:
    """Reformate basiquement le code pour le rendre lisible."""
    lines = content.split('\n')
    formatted = []
    indent = 0
    
    for line in lines:
        stripped = line.strip()
        
        # Réduire l'indentation avant les accolades fermantes
        if stripped.startswith('}') or stripped.startswith(')'):
            indent = max(0, indent - 1)
        
        # Appliquer l'indentation
        if stripped:
            formatted.append('    ' * indent + stripped)
        else:
            formatted.append('')
        
        # Augmenter l'indentation après les accolades ouvrantes
        if stripped.endswith('{') or stripped.endswith('('):
            indent += 1
        
        # Gérer les cas mixtes comme "} else {"
        if stripped.startswith('}') and stripped.endswith('{'):
            indent += 1
    
    return '\n'.join(formatted)


def main():
    if len(sys.argv) < 2:
        print("Usage: python deobfuscate_shader.py <input.shader> [output.shader]")
        print("       Si output n'est pas spécifié, crée <input>_deobfu.shader")
        sys.exit(1)
    
    input_path = Path(sys.argv[1])
    
    if not input_path.exists():
        print(f"Erreur: Fichier introuvable: {input_path}")
        sys.exit(1)
    
    if len(sys.argv) >= 3:
        output_path = Path(sys.argv[2])
    else:
        output_path = input_path.with_name(input_path.stem + '_deobfu' + input_path.suffix)
    
    print(f"Lecture de: {input_path}")
    content = input_path.read_text(encoding='utf-8', errors='replace')
    
    print("Désobfuscation en cours...")
    deobfuscated = deobfuscate(content)
    
    print(f"Écriture vers: {output_path}")
    output_path.write_text(deobfuscated, encoding='utf-8')
    
    # Stats
    original_lines = len(content.split('\n'))
    new_lines = len(deobfuscated.split('\n'))
    print(f"Terminé! {original_lines} lignes -> {new_lines} lignes")


if __name__ == '__main__':
    main()

