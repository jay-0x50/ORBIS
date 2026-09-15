import bpy,json
p=r'TestResults/CharacterPipeline/RigReview/StellaLegIntegrated03/Stella_leg_integrated_trial.blend'
bpy.ops.wm.open_mainfile(filepath=p,load_ui=False,use_scripts=False)
r=[]
for m in bpy.data.materials:
 if m.use_nodes:
  r.append({'material':m.name,'nodes':[{'name':n.name,'type':n.type,'image':getattr(getattr(n,'image',None),'name',None),'inputs':{i.name:str(i.default_value) for i in n.inputs if not i.is_linked and hasattr(i,'default_value')}} for n in m.node_tree.nodes], 'links':[(l.from_node.name,l.from_socket.name,l.to_node.name,l.to_socket.name) for l in m.node_tree.links]})
print('MATERIAL_AUDIT '+json.dumps(r))
