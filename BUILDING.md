# Building BioGTK

- Clone the repository then run the following dotnet build script based on your platform and CPU.
	- dotnet msbuild -t:BundleApp -p:RuntimeIdentifier=osx-x64 -p:Configuration=Release
	- dotnet msbuild -t:BundleApp -p:RuntimeIdentifier=osx-arm64 -p:Configuration=Release
	- dotnet msbuild BioGTKApp.csproj /t:CreateTarball /p:RuntimeIdentifier=linux-arm64 /p:Configuration=Release
	- dotnet msbuild BioGTKApp.csproj /t:CreateTarball /p:RuntimeIdentifier=linux-x64 /p:Configuration=Release

# Building Micro-SAM ONNX models.
- Recommended to just download the pre-built models from BioGTK releases. Due to the large amount of dependencies etc. required to install & run Micro-SAM repository.
- Setup Micro-SAM by cloning the [Micro-SAM](https://github.com/computational-cell-analytics/micro-sam) repository.
	- In the base directory of Micro-SAM create python files "export_encoder.py" and "export_decoder.py" and add the code provided below. 

-For exporting Micro-SAM ONNX encoder & decoder.
```
import os
import warnings
from typing import Optional, Union

import torch
from segment_anything.utils.onnx import SamOnnxModel
from ..util import get_sam_model
def export_onnx_image_encoder(
    model_type,
    output_root,
    opset: int,
    export_name: Optional[str] = None,
    checkpoint_path: Optional[Union[str, os.PathLike]] = None,
) -> None:
    """Export SAM image encoder (image -> image embeddings) to ONNX with fixed input size."""

    if export_name is None:
        export_name = model_type
    name = f"sam-{export_name}-image-encoder"

    output_folder = os.path.join(output_root, name)
    weight_output_folder = os.path.join(output_folder, "1")
    os.makedirs(weight_output_folder, exist_ok=True)

    # Ensure the model and tensors are on the same device
    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")

    # Load SAM model
    _, sam = get_sam_model(model_type=model_type, checkpoint_path=checkpoint_path, return_sam=True)
    sam.to(device)

    # Create a wrapper model for the image encoder
    class ImageEncoderModel(torch.nn.Module):
        def __init__(self, sam_model):
            super().__init__()
            self.image_encoder = sam_model.image_encoder

        def forward(self, image):
            # Pass the image through the image encoder
            image_embeddings = self.image_encoder(image)
            return image_embeddings

    image_encoder_model = ImageEncoderModel(sam).to(device)

    # Example inputs with fixed size
    dummy_inputs = {
        "image": torch.randn(1, 3, 1024, 1024, dtype=torch.float).to(device),  # Fixed input size
    }

    # Export model to ONNX
    weight_path = os.path.join(weight_output_folder, "model.onnx")
    with warnings.catch_warnings():
        warnings.filterwarnings("ignore", category=torch.jit.TracerWarning)
        warnings.filterwarnings("ignore", category=UserWarning)
        with open(weight_path, "wb") as f:
            print(f"Exporting ONNX model to {weight_path}...")
            torch.onnx.export(
                image_encoder_model,
                tuple(dummy_inputs.values()),
                f,
                export_params=True,
                verbose=False,
                opset_version=opset,
                do_constant_folding=True,
                input_names=list(dummy_inputs.keys()),
                output_names=["image_embeddings"],
                dynamic_axes=None,  # No dynamic axes
            )

    print("ONNX export completed successfully!")

    if onnxruntime_exists:
        ort_inputs = {k: _to_numpy(v) for k, v in dummy_inputs.items()}
        providers = ["CPUExecutionProvider"]
        ort_session = onnxruntime.InferenceSession(weight_path, providers=providers)
        result = ort_session.run(None, ort_inputs)
        print("Model has successfully been run with ONNXRuntime.")
        print(f"Output embeddings shape: {result[0].shape}")

```
- For exporting Micro-SAM mask decoder.
```
import os
import warnings
from typing import Optional, Union

import torch
from segment_anything.utils.onnx import SamOnnxModel
from ..util import get_sam_model
def export_decoder_onnx(
    sam_model, output_path: str, opset: int = 13,
    return_single_mask: bool = True,
    use_stability_score: bool = False,
    return_extra_metrics: bool = False
):
    device = torch.device("cpu")
    sam_model.to(device)
    
    mask_decoder = SamOnnxModel(
        model=sam_model,
        return_single_mask=return_single_mask,
        use_stability_score=use_stability_score,
        return_extra_metrics=return_extra_metrics,
    ).to(device)
    
    embed_dim = sam_model.prompt_encoder.embed_dim
    embed_size = sam_model.prompt_encoder.image_embedding_size
    mask_input_size = [4 * x for x in embed_size]
    
    dummy_inputs = {
        "image_embeddings": torch.randn(1, embed_dim, *embed_size, dtype=torch.float).to(device),
        "point_coords": torch.randint(low=0, high=1024, size=(1, 5, 2), dtype=torch.float).to(device),
        "point_labels": torch.randint(low=0, high=4, size=(1, 5), dtype=torch.float).to(device),
        "mask_input": torch.randn(1, 1, *mask_input_size, dtype=torch.float).to(device),
        "has_mask_input": torch.tensor([1], dtype=torch.float).to(device),
        "orig_im_size": torch.tensor([1024, 1024], dtype=torch.float).to(device),
    }
    
    onnx_path = os.path.join(output_path, "sam_decoder.onnx")
    with open(onnx_path, "wb") as f:
        print(f"Exporting decoder to {onnx_path}...")
        torch.onnx.export(
            mask_decoder,
            tuple(dummy_inputs.values()),
            f,
            export_params=True,
            opset_version=opset,
            do_constant_folding=True,
            input_names=list(dummy_inputs.keys()),
            output_names=["masks", "iou_predictions", "low_res_masks"],
            dynamic_axes={
                "image_embeddings": {0: "batch_size"},
                "point_coords": {1: "num_points"},
                "point_labels": {1: "num_points"},
                "masks": {0: "batch_size", 1: "num_masks", 2: "height", 3: "width"},
            },
        )
    print("Decoder ONNX export complete!")

```