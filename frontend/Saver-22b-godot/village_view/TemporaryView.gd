extends Node
const village_view_node = preload("res://village_view/village_background_nine_patch_rect.tscn")

# Called when the node enters the scene tree for the first time.
func _ready():
	var village_view_instance = village_view_node.instantiate()
	village_view_instance.set_name("view")
	add_child(village_view_instance)
	village_view_instance.initialize(1000,500,0,0, [Vector2(0,0),Vector2(1,0),Vector2(0,1),Vector2(1,1)])
