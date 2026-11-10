import numpy as np
from PIL import Image, ImageDraw, ImageFilter
src='hammer/textures/front_back.png'
im=Image.open(src).convert('RGB'); a=np.asarray(im).astype(np.float32)
lum=a.mean(2)

def find_N(x0,y0,x1,y1):
    """bounding box of the first bright glyph (the N) in the logo region"""
    sub=lum[y0:y1,x0:x1]; thr=(sub.max()+np.median(sub))/2
    m=sub>thr
    cols=np.where(m.any(0))[0]
    # first glyph: first run of columns containing bright pixels
    start=cols[0]; end=start
    while end+1<m.shape[1] and m[:,end+1].any(): end+=1
    rows=np.where(m[:,start:end+1].any(1))[0]
    return x0+start, y0+rows[0], x0+end, y0+rows[-1], thr

def redo(region):
    x0,y0,x1,y1,thr=find_N(*region)
    w,h=x1-x0+1,y1-y0+1
    sub=a[y0:y1+1,x0:x1+1]; sl=lum[y0:y1+1,x0:x1+1]
    fg=np.percentile(sub[sl>thr],85,axis=0); bg=np.median(sub[sl<thr*0.8],axis=0)
    # stem width: bright run at the left on the middle row
    row=sl[h//2]>thr; sw=0
    while sw<len(row) and row[sw]: sw+=1
    sw=max(sw,int(round(h*0.30)))
    print('N box',(x0,y0,x1,y1),'stem',sw,'fg',fg.astype(int),'bg',bg.astype(int))
    S=8; pad=2
    W,H=(w+2*pad)*S,(h+2*pad)*S
    mask=Image.new('L',(W,H),0); d=ImageDraw.Draw(mask)
    L=pad*S; R=(pad+w)*S; T=pad*S; B=(pad+h)*S; s=sw*S
    d.rectangle([L,T,L+s,B],fill=255)            # left stem
    d.rectangle([R-s,T,R,B],fill=255)            # right stem
    mid=(L+R)/2; vy=T+(B-T)*0.62                  # V of the M
    d.polygon([(L,T),(L+s*1.25,T),(mid+s*0.62,vy-s*0.2),(mid-s*0.62,vy-s*0.2)],fill=255)
    d.polygon([(R,T),(R-s*1.25,T),(mid-s*0.62,vy-s*0.2),(mid+s*0.62,vy-s*0.2)],fill=255)
    d.polygon([(mid-s*0.62,vy-s*0.2),(mid+s*0.62,vy-s*0.2),(mid,vy+s*0.9)],fill=255)
    mask=mask.resize((w+2*pad,h+2*pad),Image.LANCZOS).filter(ImageFilter.GaussianBlur(0.35))
    m=np.asarray(mask).astype(np.float32)[...,None]/255
    bx0,by0=x0-pad,y0-pad
    patch=a[by0:by0+h+2*pad,bx0:bx0+w+2*pad].copy()
    # erase the old N inside its box only (keep label texture around it)
    noise=np.random.default_rng(3).normal(0,2.0,patch.shape)
    inner=np.zeros(patch.shape[:2],bool); inner[pad-1:pad+h+1,pad-1:pad+w+1]=True
    patch[inner]=bg+noise[inner]
    patch=patch*(1-m)+fg*m
    a[by0:by0+h+2*pad,bx0:bx0+w+2*pad]=patch

redo((182,255,215,282))   # front logo
redo((722,228,762,264))   # back logo
out=Image.fromarray(np.clip(a,0,255).astype(np.uint8))
out.save('front_back_mokia.png')
out.crop((170,245,310,290)).resize((560,180),Image.NEAREST).save('front_logo2.png')
out.crop((700,210,880,280)).resize((540,210),Image.NEAREST).save('back_logo2.png')
