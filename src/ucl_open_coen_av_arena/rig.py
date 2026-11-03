from typing import Literal, Dict
from pydantic import Field

from ucl_open.rigs.base import BaseSchema
import ucl_open.rigs.device as Device

from ucl_open_coen_av_arena import __semver__


class UclOpenCoenAvArenaRig(BaseSchema):
    version: Literal[__semver__] = __semver__
    ...