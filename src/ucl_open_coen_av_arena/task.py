# Import core types
from typing import Literal
from pydantic import Field

from swc.aeon.io import reader
from swc.aeon.schema import BaseSchema, data_reader

from ucl_open_coen_av_arena import __semver__

# TODO - should inherit from some TaskParameters base class rather than BaseSchema
class UclOpenCoenAvArenaTaskParameters(BaseSchema):
    ...


class UclOpenCoenAvArenaTaskLogic(BaseSchema):
    version: Literal[__semver__] = __semver__
    name: Literal["UclOpenCoenAvArena"] = Field(default="UclOpenCoenAvArena", description="Name of the task logic", frozen=True)
    task_parameters: UclOpenCoenAvArenaTaskParameters = Field(description="Parameters of the task logic")
    ...