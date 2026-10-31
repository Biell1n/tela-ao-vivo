# Gera o ícone do Tela ao Vivo Áudio (mesma marca do site: quadrado grafite + ponto laranja + ondas)
# Uso: python gerar_icone.py  ->  icone.ico
from PIL import Image, ImageDraw

def desenhar(t):
    s = 4  # desenha grande e reduz, pra borda ficar lisa
    T = t * s
    im = Image.new('RGBA', (T, T), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([0, 0, T - 1, T - 1], radius=int(T * 0.24), fill=(20, 21, 24, 255))
    cx, cy = T * 0.40, T * 0.5
    r = T * 0.15
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(255, 91, 55, 255))
    w = max(2, int(T * 0.07))
    for k, a in ((1, 255), (2, 150)):
        R = r + T * 0.13 * k
        d.arc([cx - R, cy - R, cx + R, cy + R], start=-50, end=50, fill=(255, 91, 55, a), width=w)
    return im.resize((t, t), Image.LANCZOS)

tamanhos = [16, 20, 24, 32, 40, 48, 64, 128, 256]
base = desenhar(256)
base.save('icone.ico', sizes=[(t, t) for t in tamanhos], append_images=[desenhar(t) for t in tamanhos])
desenhar(64).save('icone.png')
print('ok')
