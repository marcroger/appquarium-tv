#!/usr/bin/env python
# Genera los ficheros de respuesta de Roslyn a partir de los .csproj que escribe Unity.
# Lo usa Tools/compile-check.sh; ver alli el porque.
import re, io, os, sys

proj, out = sys.argv[1], os.path.abspath(sys.argv[2])
NL = chr(10)

def escribir(ruta, texto):
    # AVISO: newline='' es OBLIGATORIO. Sin eso python en Windows convierte cada salto
    # de linea en CR+LF, y el shell que lee orden.txt con $(cat ...) no parte por CR:
    # el nombre salia 'Assembly-CSharp<CR>' y el csc buscaba un fichero con un retorno
    # de carro dentro. Daba CS2011 'no puedo abrir el fichero de respuesta' sobre un
    # fichero que estaba ahi y que se abria a mano sin problema.
    with io.open(ruta, 'w', encoding='utf-8', newline='') as f:
        f.write(texto)
        f.flush()
        os.fsync(f.fileno())

def rsp(proyecto, extra_refs):
    s    = io.open(proyecto, encoding='utf-8-sig').read()
    comp = re.findall(r'<Compile Include="([^"]+)"', s)
    refs = re.findall(r'<HintPath>([^<]+)</HintPath>', s)
    defs = re.findall(r'<DefineConstants>([^<]*)</DefineConstants>', s)
    if not comp:
        sys.exit("FALLO: %s no lista ningun <Compile Include> — csproj vacio o formato nuevo" % proyecto)
    name   = os.path.splitext(os.path.basename(proyecto))[0]
    outdll = os.path.join(out, name + '.dll')
    lines  = ['-target:library', '-nologo', '-nostdlib+', '-noconfig', '-langversion:9.0',
              '-unsafe+', '-out:"%s"' % outdll]
    if defs: lines.append('-define:' + defs[0])
    lines += ['-r:"%s"' % r for r in refs + extra_refs]
    lines += ['"%s"' % os.path.abspath(c) for c in comp]
    # ⚠ Cerrar Y sincronizar a disco antes de que el csc lo abra: escribiendo «al vuelo»
    # (`io.open(...).write(...)`) el fichero acababa de nacer y el csc daba CS2011 «no puedo
    # abrir el fichero de respuesta» sobre un fichero que existía. Es una carrera, así que
    # aparecía y desaparecía según la pasada.
    escribir(os.path.join(out, name + '.rsp'), NL.join(lines))
    print("%s: %d fuentes, %d referencias" % (name, len(comp), len(refs) + len(extra_refs)))
    return outdll

s = io.open(proj, encoding='utf-8-sig').read()
orden, extra = [], []
# El csproj del Editor referencia Assembly-CSharp con <ProjectReference>, no con HintPath:
# hay que compilarlo antes y pasarlo con -r:, o salen CS0246 que NO son fallos del codigo.
for dep in re.findall(r'<ProjectReference Include="([^"]+)"', s):
    dep = dep.replace(chr(92), os.sep)
    if not os.path.isfile(dep): continue
    extra.append(rsp(dep, []))
    orden.append(os.path.splitext(os.path.basename(dep))[0])
rsp(proj, extra)
orden.append(os.path.splitext(os.path.basename(proj))[0])
escribir(os.path.join(out, 'orden.txt'), NL.join(orden))
